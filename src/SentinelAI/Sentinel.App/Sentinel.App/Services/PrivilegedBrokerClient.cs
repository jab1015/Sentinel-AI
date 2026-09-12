/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Buffers;
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
    private const int MaximumProtectedRecordBytes = 64 * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions ProtectedRecordJsonOptions = new() { MaxDepth = 32 };
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

    internal Task<BrokerInvocationResult> BlockFirewallEndpointAsync(string remoteIp, CancellationToken token = default) =>
        InvokeAsync(new BrokerRequest(ProtocolVersion, NewRequestId(), "firewall-block-endpoint", RemoteIp: remoteIp), DefaultTimeout, token);

    internal Task<BrokerInvocationResult> RemoveFirewallEndpointAsync(string remoteIp, CancellationToken token = default) =>
        InvokeAsync(new BrokerRequest(ProtocolVersion, NewRequestId(), "firewall-remove-endpoint", RemoteIp: remoteIp), DefaultTimeout, token);

    internal Task<BrokerInvocationResult> RestartServiceAsync(string serviceName, CancellationToken token = default) =>
        InvokeAsync(new BrokerRequest(ProtocolVersion, NewRequestId(), "restart-service", ServiceName: serviceName), DefaultTimeout, token);

    internal async Task<BrokerQuarantineRecord?> ReadProtectedRecordAsync(string itemId, CancellationToken token = default)
    {
        if (!Guid.TryParseExact(itemId, "N", out _)) return null;
        string path = Path.Combine(_recordsRoot, itemId + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumProtectedRecordBytes) return null;
            return await JsonSerializer.DeserializeAsync<BrokerQuarantineRecord>(stream, ProtectedRecordJsonOptions, token).ConfigureAwait(false);
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
                if (stream.Length is <= 0 or > MaximumProtectedRecordBytes) continue;
                BrokerQuarantineRecord? record = await JsonSerializer.DeserializeAsync<BrokerQuarantineRecord>(stream, ProtectedRecordJsonOptions, token).ConfigureAwait(false);
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
        if (timeout <= TimeSpan.Zero)
            return BrokerInvocationResult.Failure("InvalidRequest", "The privileged request timeout was invalid.");

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(request);
        if (payload.Length is <= 0 or > MaximumRequestBytes)
            return BrokerInvocationResult.Failure("InvalidRequest", "The privileged request exceeded Sentinel's allowed message size.");

        string pipeToken = Guid.NewGuid().ToString("N");
        string pipeName = "SentinelAI.Broker." + pipeToken;

        using CancellationTokenSource operationDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        operationDeadline.CancelAfter(timeout);
        CancellationToken operationToken = operationDeadline.Token;

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
            await pipe.ConnectAsync(operationToken).WaitAsync(TimeSpan.FromSeconds(20), operationToken).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint connectedPid) || connectedPid != process.Id)
            {
                _ = TerminateBroker(process);
                return BrokerInvocationResult.Failure("BrokerIdentityMismatch", "The privileged IPC peer was not the broker process Sentinel launched.");
            }

            using StreamWriter writer = new(pipe, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

            string requestJson = Encoding.UTF8.GetString(payload);
            await writer.WriteLineAsync(requestJson).WaitAsync(operationToken).ConfigureAwait(false);
            string? responseJson = await ReadBoundedUtf8LineAsync(pipe, MaximumRequestBytes, operationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                _ = TerminateBroker(process);
                return BrokerInvocationResult.Failure("InvalidResult", "The broker returned an empty, malformed, or oversized result.");
            }

            BrokerResult? result = JsonSerializer.Deserialize<BrokerResult>(responseJson);
            if (result is null || !result.RequestId.Equals(request.RequestId, StringComparison.Ordinal))
            {
                _ = TerminateBroker(process);
                return BrokerInvocationResult.Failure("InvalidResult", "The broker result could not be matched to this request ID.");
            }

            await process.WaitForExitAsync(operationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return BrokerInvocationResult.Failure(
                    "BrokerExitFailure",
                    $"The privileged broker returned a result but exited abnormally with code {process.ExitCode}. Sentinel did not accept the operation as successful.");
            }

            return new(result.Succeeded, result.Code ?? string.Empty, result.Message ?? string.Empty, result.ItemId ?? string.Empty, result.Sha256 ?? string.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            bool brokerExitVerified = TerminateBroker(process);
            return BrokerInvocationResult.Failure(
                "Canceled",
                BuildTerminationMessage(
                    "The privileged operation was canceled.",
                    brokerExitVerified,
                    "Sentinel will verify system state before making any success claim."));
        }
        catch (OperationCanceledException)
        {
            bool brokerExitVerified = TerminateBroker(process);
            return BrokerInvocationResult.Failure(
                "Timeout",
                BuildTerminationMessage(
                    "The privileged operation exceeded its single wall-clock verification window.",
                    brokerExitVerified,
                    "Sentinel did not report success."));
        }
        catch (TimeoutException)
        {
            bool brokerExitVerified = TerminateBroker(process);
            return BrokerInvocationResult.Failure(
                "Timeout",
                BuildTerminationMessage(
                    "The privileged operation could not establish its authenticated IPC channel within the bounded connection window.",
                    brokerExitVerified,
                    "Sentinel did not report success."));
        }
        catch (IOException)
        {
            _ = TerminateBroker(process);
            return BrokerInvocationResult.Failure("IpcFailure", "The authenticated privileged IPC channel failed. Sentinel made no success claim.");
        }
        catch (DecoderFallbackException)
        {
            _ = TerminateBroker(process);
            return BrokerInvocationResult.Failure("InvalidResult", "The privileged broker returned malformed UTF-8 result data.");
        }
        catch (JsonException)
        {
            _ = TerminateBroker(process);
            return BrokerInvocationResult.Failure("InvalidResult", "The privileged broker returned malformed result data.");
        }
    }

    private static async Task<string?> ReadBoundedUtf8LineAsync(Stream stream, int maximumBytes, CancellationToken token)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            using MemoryStream line = new(capacity: Math.Min(maximumBytes, 4096));
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBytes + 1)), token).ConfigureAwait(false);
                if (read == 0) return null;

                int newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
                int segmentLength = newline >= 0 ? newline : read;
                if (line.Length + segmentLength > maximumBytes) return null;
                line.Write(buffer, 0, segmentLength);

                if (newline >= 0)
                {
                    byte[] bytes = line.ToArray();
                    int length = bytes.Length;
                    if (length > 0 && bytes[length - 1] == (byte)'\r') length--;
                    return length == 0 ? null : StrictUtf8.GetString(bytes, 0, length);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static string BuildTerminationMessage(string prefix, bool brokerExitVerified, string suffix) =>
        brokerExitVerified
            ? $"{prefix} Sentinel requested process-tree termination and verified the broker exited. {suffix}"
            : $"{prefix} Sentinel requested process-tree termination but could not verify the broker exited. {suffix}";

    private static bool TerminateBroker(Process process)
    {
        try
        {
            if (process.HasExited)
                return true;

            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5_000))
                return false;

            return process.HasExited;
        }
        catch
        {
            return false;
        }
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
        bool TerminateDescendants = false,
        string? RemoteIp = null,
        string? ServiceName = null);

    private sealed record BrokerResult(string RequestId, bool Succeeded, string? Code, string? Message, string? ItemId, string? Sha256);
}

internal sealed record BrokerInvocationResult(bool Succeeded, string Code, string Message, string ItemId, string Sha256)
{
    internal static BrokerInvocationResult Failure(string code, string message) => new(false, code, message, string.Empty, string.Empty);
}

internal sealed record BrokerQuarantineRecord(string ItemId, string OriginalPath, string Sha256, DateTimeOffset QuarantinedAtUtc);