/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    public sealed class ProcessContainmentService
    {
        private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon",
            "services", "lsass", "svchost", "dwm", "explorer", "Sentinel.App"
        };

        public async Task<ProcessContainmentResult> ContainAsync(string processName)
        {
            string normalizedName = NormalizeProcessName(processName);
            if (string.IsNullOrWhiteSpace(normalizedName))
                return ProcessContainmentResult.Failure("Process target is invalid", "Sentinel could not identify the process to contain.");

            if (ProtectedProcessNames.Contains(normalizedName))
                return ProcessContainmentResult.Failure("Process containment was blocked", $"Sentinel will not terminate protected Windows or Sentinel process {normalizedName}.");

            Process[] matches;
            try { matches = Process.GetProcessesByName(normalizedName); }
            catch (Exception ex)
            {
                return ProcessContainmentResult.Failure("Process could not be inspected", $"Sentinel could not safely enumerate the requested process. {ex.Message}");
            }

            try
            {
                if (matches.Length == 0)
                    return new(false, true, normalizedName, null, "Process is no longer running", "Sentinel rechecked the approved process and found that it had already exited. No change was needed.");

                if (matches.Length != 1)
                    return ProcessContainmentResult.Failure("Process target is ambiguous", $"Sentinel found {matches.Length} running instances of {normalizedName}. It will not terminate multiple processes from a name-only approval.");

                return await ContainAsync(normalizedName, matches[0].Id).ConfigureAwait(false);
            }
            finally
            {
                foreach (Process process in matches)
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }

        public async Task<ProcessContainmentResult> ContainAsync(string expectedProcessName, int processId)
        {
            string normalizedName = NormalizeProcessName(expectedProcessName);
            if (string.IsNullOrWhiteSpace(normalizedName) || processId <= 4)
                return ProcessContainmentResult.Failure("Process target is invalid", "Sentinel could not verify a safe process target.");

            if (ProtectedProcessNames.Contains(normalizedName))
                return ProcessContainmentResult.Failure("Process containment was blocked", $"Sentinel will not terminate protected Windows or Sentinel process {normalizedName}.");

            Process target;
            try { target = Process.GetProcessById(processId); }
            catch (ArgumentException)
            {
                return new(false, true, normalizedName, processId, "Process is no longer running", "Sentinel rechecked the approved process instance and found that it had already exited. No change was needed.");
            }
            catch (Exception ex)
            {
                return ProcessContainmentResult.Failure("Process could not be inspected", $"Sentinel could not safely inspect PID {processId}. {ex.Message}");
            }

            using (target)
            {
                string actualName;
                try { actualName = target.ProcessName; }
                catch (Exception ex)
                {
                    return ProcessContainmentResult.Failure("Process identity could not be verified", $"Sentinel could not verify PID {processId}. {ex.Message}");
                }

                if (!actualName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
                    return ProcessContainmentResult.Failure("Process identity changed", $"PID {processId} is now {actualName}, not the approved process {normalizedName}. Sentinel made no change.");
            }

            try
            {
                int exitCode = await RunTaskKillElevatedAsync(processId).ConfigureAwait(false);
                if (exitCode != 0)
                    return ProcessContainmentResult.Failure("Process could not be contained", $"Windows returned exit code {exitCode}. Sentinel did not report containment as successful.");

                bool exited = await VerifyExitedAsync(processId).ConfigureAwait(false);
                if (!exited)
                    return ProcessContainmentResult.Failure("Process containment could not be verified", "The termination command completed, but Sentinel still detected the approved process instance.");

                return new(true, true, normalizedName, processId, "Suspicious process contained", $"Sentinel stopped and verified termination of {normalizedName} (PID {processId}). Monitoring will continue for recurrence or persistence.");
            }
            catch (Exception ex)
            {
                return ProcessContainmentResult.Failure("Process containment could not complete", $"Sentinel did not report containment as successful because verification did not complete. {ex.Message}");
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
            try { using Process process = Process.GetProcessById(processId); return !process.HasExited; }
            catch (ArgumentException) { return false; }
            catch { return true; }
        }

        private static string NormalizeProcessName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
        }

        public sealed record ProcessContainmentResult(bool Attempted, bool Succeeded, string ProcessName, int? ProcessId, string Title, string Summary)
        {
            public static ProcessContainmentResult Failure(string title, string summary) => new(true, false, string.Empty, null, title, summary);
        }
    }
}
