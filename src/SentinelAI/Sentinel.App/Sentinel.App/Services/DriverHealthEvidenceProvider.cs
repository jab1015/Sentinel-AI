/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Collects local Windows driver and Plug-and-Play evidence without guessing.
    /// </summary>
    public sealed class DriverHealthEvidenceProvider
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

        public string GetDriverHealthStatus()
        {
            string value = RunPowerShell(
                "$devices=@(Get-CimInstance Win32_PnPEntity -ErrorAction Stop); " +
                "$problems=@($devices | Where-Object {$_.ConfigManagerErrorCode -ne 0}); " +
                "$signed=@(Get-CimInstance Win32_PnPSignedDriver -ErrorAction SilentlyContinue); " +
                "$unsigned=@($signed | Where-Object {$_.IsSigned -eq $false}); " +
                "$recent=@(Get-WinEvent -FilterHashtable @{LogName='System';StartTime=(Get-Date).AddDays(-7)} -ErrorAction SilentlyContinue | " +
                "Where-Object {$_.ProviderName -match 'Kernel-PnP|DriverFrameworks|UserPnp'} | Select-Object -First 5); " +
                "$problemText=@($problems | Select-Object -First 5 | ForEach-Object {\"$($_.Name) (Code $($_.ConfigManagerErrorCode))\"}) -join ' | '; " +
                "$eventText=@($recent | Select-Object -First 3 | ForEach-Object {\"$($_.ProviderName) Event $($_.Id)\"}) -join ' | '; " +
                "\"Devices=$($devices.Count);ProblemDevices=$($problems.Count);UnsignedDrivers=$($unsigned.Count);RecentDriverEvents=$($recent.Count);Problems=$problemText;Events=$eventText\"");

            if (string.IsNullOrWhiteSpace(value))
            {
                return "Sentinel could not verify driver health because Windows did not expose driver information to this process.";
            }

            Dictionary<string, string> evidence = ParseEvidence(value);
            int devices = ReadInt(evidence, "Devices");
            int problemDevices = ReadInt(evidence, "ProblemDevices");
            int unsignedDrivers = ReadInt(evidence, "UnsignedDrivers");
            int recentEvents = ReadInt(evidence, "RecentDriverEvents");
            string problems = ReadText(evidence, "Problems");

            if (problemDevices == 0)
            {
                string signatureNote = unsignedDrivers > 0
                    ? $" Windows also reported {unsignedDrivers} driver signature record(s) that require additional review before Sentinel can determine whether they matter."
                    : string.Empty;
                string eventNote = recentEvents > 0
                    ? $" Sentinel found {recentEvents} recent driver-related Windows event(s), but no device currently reports a problem."
                    : " No recent driver failures were found.";

                return $"Your drivers appear healthy. Sentinel checked {devices} devices and none currently report a Device Manager problem.{eventNote}{signatureNote} No action is required right now.";
            }

            string deviceSummary = string.IsNullOrWhiteSpace(problems)
                ? $"Windows reports {problemDevices} device(s) with a driver or device problem."
                : problemDevices == 1
                    ? $"Windows reports one device that needs attention: {problems}."
                    : $"Windows reports {problemDevices} devices that need attention: {problems}.";

            string eventSummary = recentEvents == 0
                ? " No recent driver-related failures were found in the Windows System log."
                : $" Sentinel also found {recentEvents} recent driver-related Windows event(s).";

            string signatureSummary = unsignedDrivers == 0
                ? string.Empty
                : $" Windows reported {unsignedDrivers} driver signature record(s) that require additional review; Sentinel is not labeling them unsafe without more evidence.";

            return $"Your driver health needs attention. Sentinel checked {devices} devices. {deviceSummary}{eventSummary}{signatureSummary} Recommended action: check Windows Update and your computer manufacturer's support page for an updated driver for the listed device. If the device is not used, no immediate action may be necessary.";
        }

        private static Dictionary<string, string> ParseEvidence(string value)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string segment in value.Replace("\r", string.Empty, StringComparison.Ordinal)
                         .Replace("\n", string.Empty, StringComparison.Ordinal)
                         .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int separator = segment.IndexOf('=');
                if (separator <= 0) continue;
                result[segment[..separator].Trim()] = segment[(separator + 1)..].Trim();
            }
            return result;
        }

        private static int ReadInt(IReadOnlyDictionary<string, string> evidence, string key) =>
            evidence.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed) ? parsed : 0;

        private static string ReadText(IReadOnlyDictionary<string, string> evidence, string key) =>
            evidence.TryGetValue(key, out string? value) ? value.Trim() : string.Empty;

        private static string RunPowerShell(string command)
        {
            try
            {
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                if (!process.Start()) return string.Empty;
                string output = process.StandardOutput.ReadToEnd();
                _ = process.StandardError.ReadToEnd();

                if (!process.WaitForExit((int)CommandTimeout.TotalMilliseconds))
                {
                    process.Kill(true);
                    return string.Empty;
                }

                return process.ExitCode == 0 ? output.Trim() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
