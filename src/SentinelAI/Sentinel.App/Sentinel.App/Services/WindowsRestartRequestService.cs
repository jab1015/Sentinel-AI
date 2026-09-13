using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal static class WindowsRestartRequestService
{
    private static readonly TimeSpan RestartRequestTimeout = TimeSpan.FromSeconds(10);

    internal static Task<ProcessExecutionResult> RequestRestartAsync(CancellationToken cancellationToken = default)
    {
        string shutdownPath = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
        if (!Path.IsPathFullyQualified(shutdownPath) || !File.Exists(shutdownPath))
            return Task.FromResult(ProcessExecutionResult.LaunchFailure("The trusted Windows restart executable could not be resolved."));

        ProcessStartInfo startInfo = new()
        {
            FileName = shutdownPath,
            Arguments = "/r /t 0",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return BoundedProcessRunner.RunAsync(
            startInfo,
            RestartRequestTimeout,
            cancellationToken,
            maxOutputChars: 16_384);
    }
}
