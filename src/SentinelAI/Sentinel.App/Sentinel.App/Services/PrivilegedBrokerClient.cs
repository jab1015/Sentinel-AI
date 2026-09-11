/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class PrivilegedBrokerClient
{
    private const int ProtocolVersion = 1;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    private readonly string _brokerPath;
    private readonly string _resultsRoot;
    private readonly string _recordsRoot;

    internal PrivilegedBrokerClient(string? brokerPath = null)
    {
        _brokerPath = brokerPath ?? Path.Combine(AppContext.BaseDirectory, "Sentinel.PrivilegedBroker.exe");
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SentinelAI", "Broker");
        _resultsRoot = Path.Combine(root, "Results");
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
            ProtocolVersion,
            NewRequestId(),
            "terminate-process",
            ProcessId: processId,
            ExpectedProcessStartUtcTicks: expectedStartUtc.UtcTicks,
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

        string resultPath = Path.Combine(_resultsRoot, request.RequestId + ".json");
        string json = JsonSerializer.Serialize(request);
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _brokerPath,
                Arguments = "--request " + payload,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

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

        try
        {
            await process.WaitForExitAsync(token).WaitAsync(timeout, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return BrokerInvocationResult.Failure("Canceled", "The privileged operation was canceled. Sentinel will verify system state before making any success claim.");
        }
        catch (TimeoutException)
        {
            return BrokerInvocationResult.Failure("Timeout", "The privileged operation exceeded its verification window. Sentinel did not report success.");
        }

        for (int attempt = 0; attempt < 20 && !File.Exists(resultPath); attempt++)
            await Task.Delay(50, token).ConfigureAwait(false);

        if (!File.Exists(resultPath))
            return BrokerInvocationResult.Failure("ResultMissing", $"The broker exited with code {process.ExitCode}, but no protected result record was available.");

        try
        {
            await using FileStream stream = new(resultPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            BrokerResult? result = await JsonSerializer.DeserializeAsync<BrokerResult>(stream, cancellationToken: token).ConfigureAwait(false);
            if (result is null || !result.RequestId.Equals(request.RequestId, StringComparison.Ordinal))
                return BrokerInvocationResult.Failure("InvalidResult", "The broker result could not be authenticated to this request ID.");

            return new(result.Succeeded, result.Code ?? string.Empty, result.Message ?? string.Empty, result.ItemId ?? string.Empty, result.Sha256 ?? string.Empty);
        }
        catch (Exception ex)
        {
            return BrokerInvocationResult.Failure("ResultReadFailure", $"The broker result could not be read ({ex.GetType().Name}).");
        }
    }

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
