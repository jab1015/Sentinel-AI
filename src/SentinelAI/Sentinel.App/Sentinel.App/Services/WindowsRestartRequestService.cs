using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal static class WindowsRestartRequestService
{
    private static readonly TimeSpan RestartRequestTimeout = TimeSpan.FromSeconds(10);

    internal static Task<ProcessExecutionResult> RequestRestartAsync(CancellationToken cancellationToken = default)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "shutdown.exe",
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
