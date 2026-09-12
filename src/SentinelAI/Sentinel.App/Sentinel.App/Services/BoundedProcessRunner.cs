/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal static class BoundedProcessRunner
{
    internal const int DefaultMaxOutputChars = 1_000_000;

    internal static async Task<ProcessExecutionResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        int maxOutputChars = DefaultMaxOutputChars)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (maxOutputChars < 1) throw new ArgumentOutOfRangeException(nameof(maxOutputChars));
        if (startInfo.UseShellExecute)
            return ProcessExecutionResult.LaunchFailure("BoundedProcessRunner requires UseShellExecute=false so output ownership is explicit.");

        if (ChildProcessSafetyPolicy.IsBlocked(
            new ChildProcessSafetyPolicy.ProcessStartInfoLike(startInfo.FileName ?? string.Empty),
            out string blockedReason))
        {
            return ProcessExecutionResult.LaunchFailure(blockedReason);
        }

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                return ProcessExecutionResult.LaunchFailure("The child process did not start.");
        }
        catch (Exception ex)
        {
            return ProcessExecutionResult.LaunchFailure(ex.GetType().Name);
        }

        Task<BoundedReadResult> stdoutTask = DrainAsync(process.StandardOutput, maxOutputChars);
        Task<BoundedReadResult> stderrTask = DrainAsync(process.StandardError, maxOutputChars);
        Task exitTask = process.WaitForExitAsync();
        Task timeoutTask = Task.Delay(timeout);
        Task cancellationTask = cancellationToken.CanBeCanceled
            ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
            : Task.Delay(Timeout.InfiniteTimeSpan);

        Task completed;
        try
        {
            completed = await Task.WhenAny(exitTask, timeoutTask, cancellationTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            completed = cancellationTask;
        }

        ProcessExecutionOutcome outcome;
        if (completed == exitTask)
        {
            outcome = ProcessExecutionOutcome.Completed;
        }
        else if (completed == cancellationTask || cancellationToken.IsCancellationRequested)
        {
            outcome = ProcessExecutionOutcome.Canceled;
            TryKillTree(process);
        }
        else
        {
            outcome = ProcessExecutionOutcome.TimedOut;
            TryKillTree(process);
        }

        if (outcome != ProcessExecutionOutcome.Completed)
        {
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch { }
        }

        BoundedReadResult stdout;
        BoundedReadResult stderr;
        try
        {
            stdout = await stdoutTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            stderr = await stderrTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            return new ProcessExecutionResult(
                ProcessExecutionOutcome.OutputReadFailure,
                ExitCode: TryGetExitCode(process),
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                OutputTruncated: false,
                Detail: "Child output could not be drained safely.");
        }

        if (outcome == ProcessExecutionOutcome.TimedOut)
            return new(outcome, TryGetExitCode(process), stdout.Text, stderr.Text, stdout.Truncated || stderr.Truncated, "The child exceeded its wall-clock timeout and Sentinel terminated its process tree.");
        if (outcome == ProcessExecutionOutcome.Canceled)
            return new(outcome, TryGetExitCode(process), stdout.Text, stderr.Text, stdout.Truncated || stderr.Truncated, "The operation was canceled and Sentinel terminated its child process tree.");

        int exitCode = process.ExitCode;
        return new(
            exitCode == 0 ? ProcessExecutionOutcome.Succeeded : ProcessExecutionOutcome.NonZeroExit,
            exitCode,
            stdout.Text,
            stderr.Text,
            stdout.Truncated || stderr.Truncated,
            exitCode == 0 ? "The child completed successfully." : $"The child exited with code {exitCode}.");
    }

    private static async Task<BoundedReadResult> DrainAsync(StreamReader reader, int maxChars)
    {
        char[] buffer = new char[4096];
        StringBuilder captured = new(Math.Min(maxChars, 16_384));
        bool truncated = false;

        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
            if (read == 0) break;

            int remaining = maxChars - captured.Length;
            if (remaining > 0)
                captured.Append(buffer, 0, Math.Min(remaining, read));
            if (read > remaining)
                truncated = true;
        }

        return new(captured.ToString(), truncated);
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The process may have exited or Windows may deny termination. The caller
            // receives a non-success outcome and must never claim the command completed.
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try { return process.HasExited ? process.ExitCode : null; }
        catch { return null; }
    }

    private sealed record BoundedReadResult(string Text, bool Truncated);
}

internal enum ProcessExecutionOutcome
{
    Succeeded,
    NonZeroExit,
    TimedOut,
    Canceled,
    LaunchFailure,
    OutputReadFailure,
    Completed
}

internal sealed record ProcessExecutionResult(
    ProcessExecutionOutcome Outcome,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool OutputTruncated,
    string Detail)
{
    internal bool Succeeded => Outcome == ProcessExecutionOutcome.Succeeded;

    internal static ProcessExecutionResult LaunchFailure(string detail) =>
        new(ProcessExecutionOutcome.LaunchFailure, null, string.Empty, string.Empty, false, detail);
}
