using System.Diagnostics;
using System.Text;

internal static class BoundedProcessRunner
{
    private const int DefaultMaxOutputChars = 128_000;
    private static readonly TimeSpan PostTerminationWait = TimeSpan.FromSeconds(5);

    internal static async Task<BoundedProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        int maxOutputChars = DefaultMaxOutputChars)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (maxOutputChars < 1) throw new ArgumentOutOfRangeException(nameof(maxOutputChars));
        if (startInfo.UseShellExecute)
            return BoundedProcessResult.Fail("LaunchPolicyRejected", "The broker requires UseShellExecute=false for owned child processes.");

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                return BoundedProcessResult.Fail("LaunchFailed", "The child process did not start.");
        }
        catch (Exception ex)
        {
            return BoundedProcessResult.Fail("LaunchFailed", $"The child process could not start ({ex.GetType().Name}).");
        }

        Task<BoundedReadResult> stdoutTask = DrainAsync(process.StandardOutput, maxOutputChars);
        Task<BoundedReadResult> stderrTask = DrainAsync(process.StandardError, maxOutputChars);
        Task exitTask = process.WaitForExitAsync();
        Task timeoutTask = Task.Delay(timeout);
        Task cancellationTask = cancellationToken.CanBeCanceled
            ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
            : Task.Delay(Timeout.InfiniteTimeSpan);

        Task completed = await Task.WhenAny(exitTask, timeoutTask, cancellationTask).ConfigureAwait(false);
        bool canceled = completed == cancellationTask || cancellationToken.IsCancellationRequested;
        bool timedOut = !canceled && completed != exitTask;

        bool terminationConfirmed = true;
        if (canceled || timedOut)
        {
            terminationConfirmed = TryTerminateTree(process);
            try
            {
                await process.WaitForExitAsync().WaitAsync(PostTerminationWait).ConfigureAwait(false);
                terminationConfirmed = process.HasExited;
            }
            catch
            {
                terminationConfirmed = false;
            }
        }

        BoundedReadResult stdout;
        BoundedReadResult stderr;
        try
        {
            stdout = await stdoutTask.WaitAsync(PostTerminationWait).ConfigureAwait(false);
            stderr = await stderrTask.WaitAsync(PostTerminationWait).ConfigureAwait(false);
        }
        catch
        {
            return new(false, null, "OutputReadFailure", string.Empty, string.Empty, false, terminationConfirmed,
                "Child output could not be drained within the bounded post-termination window.");
        }

        bool truncated = stdout.Truncated || stderr.Truncated;
        if (canceled)
            return new(false, TryGetExitCode(process), "Canceled", stdout.Text, stderr.Text, truncated, terminationConfirmed,
                terminationConfirmed ? "The operation was canceled and the child process tree exited." : "The operation was canceled, but child-process termination could not be confirmed.");
        if (timedOut)
            return new(false, TryGetExitCode(process), "TimedOut", stdout.Text, stderr.Text, truncated, terminationConfirmed,
                terminationConfirmed ? "The child exceeded its wall-clock timeout and its process tree exited." : "The child exceeded its wall-clock timeout, but process-tree termination could not be confirmed.");

        int exitCode = process.ExitCode;
        return new(exitCode == 0, exitCode, exitCode == 0 ? "Success" : "NonZeroExit", stdout.Text, stderr.Text, truncated, true,
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

    private static bool TryTerminateTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try { return process.HasExited ? process.ExitCode : null; }
        catch { return null; }
    }

    private sealed record BoundedReadResult(string Text, bool Truncated);
}

internal sealed record BoundedProcessResult(
    bool Succeeded,
    int? ExitCode,
    string Code,
    string StandardOutput,
    string StandardError,
    bool OutputTruncated,
    bool TerminationConfirmed,
    string Detail)
{
    internal static BoundedProcessResult Fail(string code, string detail) =>
        new(false, null, code, string.Empty, string.Empty, false, true, detail);
}
