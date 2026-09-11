/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class PrivilegedBrokerClient
{
    private const int ProtocolVersion = 2;
    private const int MaximumRequestBytes = 32 * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    private readonly string _brokerPath;
    private readonly string _recordsRoot;

    internal PrivilegedBrokerClient(string? brokerPath = null)
    {
        _brokerPath = brokerPath ?? Path.Combine(AppContext.BaseDirectory, "Sentinel.PrivilegedBroker.exe");
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SentinelAI", "Broker");
        _recordsRoot = Path.Combine(root, "QuarantineRecords");
    }

    internal bool IsBrokerPresent => File.Exists(_brokerPath);

    internal Task<BrokerInvocationResult> QuarantineFileAsync(string sourcePath, string itemId, CancellationToken token = default) =>
        InvokeAsync(new BrokerRequest(ProtocolVersion, NewRequestId(), "quarantine-file", ItemId: itemId, SourcePath: sourcePath), DefaultTimeout, token);

    internal Task<BrokerInvocationResult> RestoreFileAsync(string itemId, CancellationToken token = default) =>
        InvokeAsync(new BrokerRequest(ProtocolVersion, NewRequestId(), "restore-quarantined-file", ItemId: itemId), DefaultTimeout, token);

    internal Task<BrokerInvocationResult> DeleteFileAsync(string itemId, CancellationToken token = default) =>
        InvokeAsync(new BrokerRequest(ProtocolVersion, NewRequestId(), "delete-quarantined-file", ItemId: itemId), DefaultTimeout, token);

    internal Task<BrokerInvocationResult> TerminateProcessAsync(
        int processId,
        DateTimeOffset expectedStartUtc,
        string expectedImagePath,
        string expectedImageSha256,
        bool terminateDescendants,
        CancellationToken token = default) =>
        InvokeAsync(new BrokerRequest(
            ProtocolVersion, NewRequestId(), "terminate-process",
            ProcessId: processId,
            ExpectedProcessStartUtcTicks: expectedStartUtc.UtcDateTime.Ticks,
            ExpectedImagePath: expectedImagePath,
            ExpectedImageSha256: expectedImageSha256,
            TerminateDescendants: terminateDescendants), DefaultTimeout, token);

    internal async Task<BrokerQuarantineRecord?> ReadProtectedRecordAsync(string itemId, CancellationToken token = default)
    {
        if (!Guid.TryParseExact(itemId, "N", out _)) return null;
        string path = Path.Combine(_recordsRoot, itemId + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<BrokerQuarantineRecord>(stream, cancellationToken: token).ConfigureAwait(false);
        }
        catch { return null; }
    }

    internal async Task<BrokerQuarantineRecord[]> ReadProtectedRecordsAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(_recordsRoot)) return Array.Empty<BrokerQuarantineRecord>();
        var records = new System.Collections.Generic.List<BrokerQuarantineRecord>();
        foreach (string path in Directory.EnumerateFiles(_recordsRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                BrokerQuarantineRecord? record = await JsonSerializer.DeserializeAsync<BrokerQuarantineRecord>(stream, cancellationToken: token).ConfigureAwait(false);
                if (record is not null && Guid.TryParseExact(record.ItemId, "N", out _)) records.Add(record);
            }
            catch { }
        }
        return records.ToArray();
    }

    internal static async Task<string?> ComputeSha256Async(string path, CancellationToken token = default)
    {
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        }
        catch { return null; }
    }

    private async Task<BrokerInvocationResult> InvokeAsync(BrokerRequest request, TimeSpan timeout, CancellationToken token)
    {
        if (!File.Exists(_brokerPath))
            return BrokerInvocationResult.Failure("BrokerUnavailable", "Sentinel's privileged broker is not installed with this build.");

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(request);
        if (payload.Length is <= 0 or > MaximumRequestBytes)
            return BrokerInvocationResult.Failure("InvalidRequest", "The privileged request exceeded Sentinel's allowed message size.");

        string pipeToken = Guid.NewGuid().ToString("N");
        string pipeName = "SentinelAI.Broker." + pipeToken;

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _brokerPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        process.StartInfo.ArgumentList.Add("--pipe");
        process.StartInfo.ArgumentList.Add(pipeToken);

        try
        {
            if (!process.Start())
                return BrokerInvocationResult.Failure("LaunchFailure", "The privileged broker did not start.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return BrokerInvocationResult.Failure("ElevationDenied", "Windows elevation was canceled or denied. Sentinel made no privileged change.");
        }
        catch (Exception ex)
        {
            return BrokerInvocationResult.Failure("LaunchFailure", $"The privileged broker could not start ({ex.GetType().Name}).");
        }

        using NamedPipeClientStream pipe = new(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(token).WaitAsync(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint connectedPid) || connectedPid != process.Id)
            {
                TerminateBroker(process);
                return BrokerInvocationResult.Failure("BrokerIdentityMismatch", "The privileged IPC peer was not the broker process Sentinel launched.");
            }

            using StreamWriter writer = new(pipe, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
            using StreamReader reader = new(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

            string requestJson = Encoding.UTF8.GetString(payload);
            await writer.WriteLineAsync(requestJson).WaitAsync(timeout, token).ConfigureAwait(false);
            string? responseJson = await reader.ReadLineAsync().WaitAsync(timeout, token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(responseJson) || Encoding.UTF8.GetByteCount(responseJson) > MaximumRequestBytes)
            {
                TerminateBroker(process);
                return BrokerInvocationResult.Failure("InvalidResult", "The broker returned an empty or oversized result.");
            }

            BrokerResult? result = JsonSerializer.Deserialize<BrokerResult>(responseJson);
            if (result is null || !result.RequestId.Equals(request.RequestId, StringComparison.Ordinal))
            {
                TerminateBroker(process);
                return BrokerInvocationResult.Failure("InvalidResult", "The broker result could not be matched to this request ID.");
            }

            await process.WaitForExitAsync(token).WaitAsync(timeout, token).ConfigureAwait(false);
            return new(result.Succeeded, result.Code ?? string.Empty, result.Message ?? string.Empty, result.ItemId ?? string.Empty, result.Sha256 ?? string.Empty);
        }
        catch (OperationCanceledException)
        {
            TerminateBroker(process);
            return BrokerInvocationResult.Failure("Canceled", "The privileged operation was canceled. Sentinel terminated the broker request and will verify system state before making any success claim.");
        }
        catch (TimeoutException)
        {
            TerminateBroker(process);
            return BrokerInvocationResult.Failure("Timeout", "The privileged operation exceeded its verification window. Sentinel terminated the broker request and did not report success.");
        }
        catch (IOException)
        {
            TerminateBroker(process);
            return BrokerInvocationResult.Failure("IpcFailure", "The authenticated privileged IPC channel failed. Sentinel made no success claim.");
        }
        catch (JsonException)
        {
            TerminateBroker(process);
            return BrokerInvocationResult.Failure("InvalidResult", "The privileged broker returned malformed result data.");
        }
    }

    private static void TerminateBroker(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch { }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);

    private static string NewRequestId() => Guid.NewGuid().ToString("N");

    private sealed record BrokerRequest(
        int Version,
        string RequestId,
        string Operation,
        string? ItemId = null,
        string? SourcePath = null,
        int ProcessId = 0,
        long ExpectedProcessStartUtcTicks = 0,
        string? ExpectedImagePath = null,
        string? ExpectedImageSha256 = null,
        bool TerminateDescendants = false);

    private sealed record BrokerResult(string RequestId, bool Succeeded, string? Code, string? Message, string? ItemId, string? Sha256);
}

internal sealed record BrokerInvocationResult(bool Succeeded, string Code, string Message, string ItemId, string Sha256)
{
    internal static BrokerInvocationResult Failure(string code, string message) => new(false, code, message, string.Empty, string.Empty);
}

internal sealed record BrokerQuarantineRecord(string ItemId, string OriginalPath, string Sha256, DateTimeOffset QuarantinedAtUtc);
