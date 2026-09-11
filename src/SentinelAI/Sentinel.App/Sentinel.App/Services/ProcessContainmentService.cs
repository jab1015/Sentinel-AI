/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    public sealed class ProcessContainmentService
    {
        private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon",
            "services", "lsass", "svchost", "dwm", "explorer", "Sentinel.App",
            "Sentinel.PrivilegedBroker", "Memory Compression", "Secure System", "LsaIso",
            "fontdrvhost", "sihost", "taskhostw", "conhost", "WmiPrvSE", "MsMpEng",
            "SecurityHealthService", "NisSrv"
        };

        private readonly PrivilegedBrokerClient _broker = new();

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
                return ProcessContainmentResult.Failure("Process could not be inspected", $"Sentinel could not safely enumerate the requested process ({ex.GetType().Name}).");
            }

            try
            {
                if (matches.Length == 0)
                    return new(false, true, normalizedName, null, "Process is no longer running", "Sentinel rechecked the approved process and found that it had already exited. No change was needed.");

                if (matches.Length != 1)
                    return ProcessContainmentResult.Failure("Process target is ambiguous", $"Sentinel found {matches.Length} running instances of {normalizedName}. It will not terminate multiple processes from a name-only approval.");

                DateTimeOffset start;
                try { start = matches[0].StartTime.ToUniversalTime(); }
                catch { return ProcessContainmentResult.Failure("Process identity could not be verified", "Sentinel could not read the target process creation time."); }

                return await ContainAsync(normalizedName, matches[0].Id, start).ConfigureAwait(false);
            }
            finally
            {
                foreach (Process process in matches)
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }

        public Task<ProcessContainmentResult> ContainAsync(string expectedProcessName, int processId) =>
            ContainAsync(expectedProcessName, processId, expectedStartTimeUtc: null);

        public async Task<ProcessContainmentResult> ContainAsync(
            string expectedProcessName,
            int processId,
            DateTimeOffset? expectedStartTimeUtc)
        {
            string normalizedName = NormalizeProcessName(expectedProcessName);
            if (string.IsNullOrWhiteSpace(normalizedName) || processId <= 4)
                return ProcessContainmentResult.Failure("Process target is invalid", "Sentinel could not verify a safe process target.");

            if (ProtectedProcessNames.Contains(normalizedName))
                return ProcessContainmentResult.Failure("Process containment was blocked", $"Sentinel will not terminate protected Windows or Sentinel process {normalizedName}.");

            if (!_broker.IsBrokerPresent)
                return ProcessContainmentResult.Failure("Process containment unavailable", "Sentinel's privileged broker is not installed with this build. No termination was attempted.");

            string imagePath;
            string imageHash;
            DateTimeOffset actualStartTimeUtc;

            try
            {
                using Process target = Process.GetProcessById(processId);
                string actualName = target.ProcessName;
                if (!actualName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
                    return ProcessContainmentResult.Failure("Process identity changed", $"PID {processId} is now {actualName}, not the approved process {normalizedName}. Sentinel made no change.");

                actualStartTimeUtc = target.StartTime.ToUniversalTime();
                if (expectedStartTimeUtc.HasValue && actualStartTimeUtc != expectedStartTimeUtc.Value)
                    return ProcessContainmentResult.Failure("Process instance changed", $"PID {processId} no longer identifies the exact process instance that was approved. Sentinel made no change.");

                imagePath = Path.GetFullPath(target.MainModule?.FileName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                    return ProcessContainmentResult.Failure("Process image could not be verified", "Sentinel could not identify the executable backing the approved process instance.");

                string? hash = await PrivilegedBrokerClient.ComputeSha256Async(imagePath).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(hash))
                    return ProcessContainmentResult.Failure("Process image could not be verified", "Sentinel could not hash the executable backing the approved process instance.");
                imageHash = hash;
            }
            catch (ArgumentException)
            {
                return new(false, true, normalizedName, processId, "Process is no longer running", "Sentinel rechecked the approved process instance and found that it had already exited. No change was needed.");
            }
            catch (Exception ex)
            {
                return ProcessContainmentResult.Failure("Process identity could not be verified", $"Sentinel could not establish exact process identity ({ex.GetType().Name}). No change was made.");
            }

            // Descendants are deliberately excluded from this approval. A future UI may
            // request explicit tree scope, but name/PID approval alone never authorizes it.
            BrokerInvocationResult result = await _broker.TerminateProcessAsync(
                processId,
                actualStartTimeUtc,
                imagePath,
                imageHash,
                terminateDescendants: false,
                CancellationToken.None).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                if (result.Code.Equals("ProcessIdentityChanged", StringComparison.OrdinalIgnoreCase) ||
                    result.Code.Equals("ProcessImageChanged", StringComparison.OrdinalIgnoreCase))
                {
                    return ProcessContainmentResult.Failure("Process identity changed before containment", "The privileged broker revalidated the process after elevation and found that it was no longer the exact approved instance. Sentinel made no change.");
                }

                return ProcessContainmentResult.Failure("Process could not be contained", $"Sentinel did not report containment success. {result.Message}");
            }

            return new(true, true, normalizedName, processId,
                "Suspicious process contained",
                $"Sentinel's privileged broker revalidated the exact process creation time, executable path, and executable hash after elevation, then terminated and verified the approved {normalizedName} process instance. Descendants were not included in this approval.");
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
