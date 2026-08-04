/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Read-only Windows Update health assessment. It correlates the Windows Update
    /// service state with recent Windows Update Client failures before any repair is
    /// considered. This layer never resets update components or changes services.
    /// </summary>
    public sealed class WindowsUpdateHealthAssessmentService
    {
        private static readonly TimeSpan FailureWindow = TimeSpan.FromDays(7);

        public WindowsUpdateHealthAssessment Assess()
        {
            CommandResult service = Run("sc.exe", "query wuauserv");
            bool serviceExists = service.ExitCode == 0;
            bool serviceRunning = service.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            bool serviceStopped = service.Output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase);

            DateTime sinceUtc = DateTime.UtcNow.Subtract(FailureWindow);
            string query = string.Format(
                CultureInfo.InvariantCulture,
                "*[System[Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and (Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime>='{0:yyyy-MM-ddTHH:mm:ss.fffZ}']]]",
                sinceUtc);

            CommandResult events = Run(
                "wevtutil.exe",
                $"qe System /q:\"{query}\" /f:text /c:20 /rd:true");

            IReadOnlyList<string> failureEvidence = ExtractFailureEvidence(events.Output);
            bool recentFailures = failureEvidence.Count > 0;

            // A stopped Windows Update service is not automatically a fault because
            // Windows may start it on demand. Persistent update failures are the
            // stronger signal used to justify repair investigation.
            bool repairInvestigationWarranted =
                serviceExists && recentFailures;

            string summary;
            if (!serviceExists)
                summary = "Sentinel could not verify the Windows Update service. No automatic repair will be attempted from incomplete evidence.";
            else if (!recentFailures)
                summary = "Sentinel found no recent Windows Update failure evidence requiring repair.";
            else if (serviceStopped)
                summary = "Sentinel found recent Windows Update failures while the update service is currently stopped. The condition warrants correlation before repair.";
            else
                summary = "Sentinel found recent Windows Update failures. Update-component health should be verified before any repair is attempted.";

            return new WindowsUpdateHealthAssessment(
                serviceExists,
                serviceRunning,
                serviceStopped,
                recentFailures,
                failureEvidence,
                repairInvestigationWarranted,
                summary,
                service.Output,
                service.Error,
                events.Error);
        }

        private static IReadOnlyList<string> ExtractFailureEvidence(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return Array.Empty<string>();

            return output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line =>
                    line.Contains("Event ID:", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Failure", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToArray();
        }

        private static CommandResult Run(string fileName, string arguments)
        {
            try
            {
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    return new CommandResult(-1, output, "Diagnostic command timed out.");
                }

                return new CommandResult(process.ExitCode, output, error);
            }
            catch (Exception ex)
            {
                return new CommandResult(-1, string.Empty, ex.Message);
            }
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record WindowsUpdateHealthAssessment(
        bool ServiceExists,
        bool ServiceRunning,
        bool ServiceStopped,
        bool RecentFailuresDetected,
        IReadOnlyList<string> FailureEvidence,
        bool RepairInvestigationWarranted,
        string Summary,
        string ServiceDiagnosticOutput,
        string ServiceDiagnosticError,
        string EventDiagnosticError);
}
