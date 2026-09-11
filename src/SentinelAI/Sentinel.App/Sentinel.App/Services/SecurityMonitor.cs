/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Sentinel.App.Services
{
    public sealed class SecurityMonitor
    {
        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(12);

        public SecurityStatusSnapshot GetStatus() => new(GetDefenderStatus(), GetFirewallStatus());

        public bool IsWindowsDefenderInstalled()
        {
            string status = GetDefenderStatus();
            return !status.Equals("Not detected", StringComparison.OrdinalIgnoreCase) &&
                   !status.Equals("Unavailable", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsFirewallInstalled() => !GetFirewallStatus().Equals("Unavailable", StringComparison.OrdinalIgnoreCase);

        public string GetSecuritySummary()
        {
            SecurityStatusSnapshot status = GetStatus();
            return $"Defender: {status.DefenderStatus} | Firewall: {status.FirewallStatus}";
        }

        private static string GetDefenderStatus()
        {
            const string command =
                "$s=Get-MpComputerStatus -ErrorAction Stop; " +
                "\"AMServiceEnabled=$($s.AMServiceEnabled)`nAntivirusEnabled=$($s.AntivirusEnabled)`nRealTimeProtectionEnabled=$($s.RealTimeProtectionEnabled)`nAntispywareEnabled=$($s.AntispywareEnabled)`nBehaviorMonitorEnabled=$($s.BehaviorMonitorEnabled)`nNISEnabled=$($s.NISEnabled)`nSignatureAge=$($s.AntivirusSignatureAge)`nRunningMode=$($s.AMRunningMode)\"";

            ProcessExecutionResult result = RunPowerShell(command);
            if (!result.Succeeded)
                return "Unavailable";

            Dictionary<string, string> values = ParseKeyValues(result.StandardOutput);
            if (!TryGetBool(values, "AMServiceEnabled", out bool serviceEnabled) ||
                !TryGetBool(values, "AntivirusEnabled", out bool antivirusEnabled) ||
                !TryGetBool(values, "RealTimeProtectionEnabled", out bool realtimeEnabled))
                return "Unavailable";

            values.TryGetValue("RunningMode", out string? runningMode);
            bool passive = !string.IsNullOrWhiteSpace(runningMode) &&
                           runningMode.Contains("Passive", StringComparison.OrdinalIgnoreCase);

            if (passive)
                return "Passive";
            if (!serviceEnabled || !antivirusEnabled || !realtimeEnabled)
                return "Disabled or inactive";

            if (values.TryGetValue("SignatureAge", out string? signatureAgeText) &&
                int.TryParse(signatureAgeText, out int signatureAgeDays) && signatureAgeDays > 3)
                return "Enabled (signatures stale)";

            return "Enabled";
        }

        private static string GetFirewallStatus()
        {
            const string command =
                "$svc=Get-Service -Name MpsSvc -ErrorAction Stop; " +
                "$p=@(Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop); " +
                "\"Service=$($svc.Status)`nCount=$($p.Count)\"; " +
                "$p | ForEach-Object {\"Profile=$($_.Name)|Enabled=$($_.Enabled)|Inbound=$($_.DefaultInboundAction)|Outbound=$($_.DefaultOutboundAction)\"}";

            ProcessExecutionResult result = RunPowerShell(command);
            if (!result.Succeeded)
                return "Unavailable";

            string[] lines = result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string service = string.Empty;
            int expectedCount = 0;
            int profileCount = 0;
            int enabledCount = 0;

            foreach (string line in lines)
            {
                if (line.StartsWith("Service=", StringComparison.OrdinalIgnoreCase))
                    service = line[8..].Trim();
                else if (line.StartsWith("Count=", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(line[6..].Trim(), out expectedCount);
                else if (line.StartsWith("Profile=", StringComparison.OrdinalIgnoreCase))
                {
                    profileCount++;
                    if (line.Contains("|Enabled=True", StringComparison.OrdinalIgnoreCase))
                        enabledCount++;
                }
            }

            if (!service.Equals("Running", StringComparison.OrdinalIgnoreCase))
                return "Disabled or inactive";
            if (expectedCount < 3 || profileCount != expectedCount)
                return "Unavailable";
            if (enabledCount == profileCount)
                return "Enabled";
            if (enabledCount == 0)
                return "Disabled";
            return $"Partial ({enabledCount} of {profileCount} active profiles enabled)";
        }

        private static ProcessExecutionResult RunPowerShell(string command)
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            ProcessStartInfo startInfo = new()
            {
                FileName = ResolvePowerShellPath(),
                Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            return BoundedProcessRunner.RunAsync(startInfo, QueryTimeout).GetAwaiter().GetResult();
        }

        private static string ResolvePowerShellPath()
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return string.IsNullOrWhiteSpace(system)
                ? "powershell.exe"
                : Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        }

        private static Dictionary<string, string> ParseKeyValues(string output)
        {
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in (output ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0) continue;
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
            return values;
        }

        private static bool TryGetBool(IReadOnlyDictionary<string, string> values, string key, out bool value)
        {
            value = false;
            return values.TryGetValue(key, out string? text) && bool.TryParse(text, out value);
        }

        public readonly record struct SecurityStatusSnapshot(string DefenderStatus, string FirewallStatus);
    }
}
