/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Performs user-approved process containment only after the caller has already
    /// validated the investigation and short-lived approval. The service refuses
    /// ambiguous or protected targets and verifies that the selected process exited.
    /// </summary>
    public sealed class ProcessContainmentService
    {
        private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "System",
            "Idle",
            "Registry",
            "smss",
            "csrss",
            "wininit",
            "winlogon",
            "services",
            "lsass",
            "svchost",
            "dwm",
            "explorer",
            "Sentinel.App"
        };

        public async Task<ProcessContainmentResult> ContainAsync(string processName)
        {
            string normalizedName = NormalizeProcessName(processName);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return ProcessContainmentResult.Failure(
                    "Process target is invalid",
                    "Sentinel could not identify the process to contain.");
            }

            if (ProtectedProcessNames.Contains(normalizedName))
            {
                return ProcessContainmentResult.Failure(
                    "Process containment was blocked",
                    $"Sentinel will not terminate protected Windows or Sentinel process {normalizedName}.");
            }

            Process[] matches;
            try
            {
                matches = Process.GetProcessesByName(normalizedName);
            }
            catch (Exception ex)
            {
                return ProcessContainmentResult.Failure(
                    "Process could not be inspected",
                    $"Sentinel could not safely enumerate the requested process. {ex.Message}");
            }

            using ProcessSet processSet = new(matches);

            if (matches.Length == 0)
            {
                return new ProcessContainmentResult(
                    Attempted: false,
                    Succeeded: true,
                    ProcessName: normalizedName,
                    ProcessId: null,
                    Title: "Process is no longer running",
                    Summary: "Sentinel rechecked the approved process and found that it had already exited. No change was needed.");
            }

            if (matches.Length != 1)
            {
                return ProcessContainmentResult.Failure(
                    "Process target is ambiguous",
                    $"Sentinel found {matches.Length} running instances of {normalizedName}. It will not terminate multiple processes from a name-only approval.");
            }

            Process target = matches[0];
            int processId = target.Id;

            if (processId <= 4)
            {
                return ProcessContainmentResult.Failure(
                    "Process containment was blocked",
                    "Sentinel will not terminate a core Windows process.");
            }

            try
            {
                int exitCode = await RunTaskKillElevatedAsync(processId).ConfigureAwait(false);
                if (exitCode != 0)
                {
                    return ProcessContainmentResult.Failure(
                        "Process could not be contained",
                        $"Windows returned exit code {exitCode}. Sentinel did not report containment as successful.");
                }

                bool exited = await VerifyExitedAsync(processId).ConfigureAwait(false);
                if (!exited)
                {
                    return ProcessContainmentResult.Failure(
                        "Process containment could not be verified",
                        "The termination command completed, but Sentinel still detected the approved process instance.");
                }

                return new ProcessContainmentResult(
                    Attempted: true,
                    Succeeded: true,
                    ProcessName: normalizedName,
                    ProcessId: processId,
                    Title: "Suspicious process contained",
                    Summary: $"Sentinel stopped and verified termination of {normalizedName} (PID {processId}). Monitoring will continue for recurrence or persistence.");
            }
            catch (Exception ex)
            {
                return ProcessContainmentResult.Failure(
                    "Process containment could not complete",
                    $"Sentinel did not report containment as successful because verification did not complete. {ex.Message}");
            }
        }

        private static async Task<int> RunTaskKillElevatedAsync(int processId)
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/PID {processId} /T /F",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            process.Start();
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }

        private static async Task<bool> VerifyExitedAsync(int processId)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (!IsProcessRunning(processId)) return true;
                await Task.Delay(500).ConfigureAwait(false);
            }

            return !IsProcessRunning(processId);
        }

        private static bool IsProcessRunning(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static string NormalizeProcessName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? trimmed[..^4]
                : trimmed;
        }

        private sealed class ProcessSet : IDisposable
        {
            private readonly Process[] _processes;

            public ProcessSet(Process[] processes) => _processes = processes;

            public void Dispose()
            {
                foreach (Process process in _processes)
                {
                    try { process.Dispose(); }
                    catch { }
                }
            }
        }

        public sealed record ProcessContainmentResult(
            bool Attempted,
            bool Succeeded,
            string ProcessName,
            int? ProcessId,
            string Title,
            string Summary)
        {
            public static ProcessContainmentResult Failure(string title, string summary) =>
                new(true, false, string.Empty, null, title, summary);
        }
    }
}
