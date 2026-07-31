/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Reviews enabled Windows Firewall rules for investigation evidence.
    /// A firewall-rule finding is not actionable by itself and must be
    /// correlated with process, network, persistence, or security evidence.
    /// </summary>
    public sealed class FirewallRuleMonitor
    {
        public FirewallRuleSnapshot GetSnapshot()
        {
            try
            {
                const string script =
                    "Get-NetFirewallRule -Enabled True -ErrorAction SilentlyContinue | " +
                    "ForEach-Object { $rule=$_; $app=$rule | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue; " +
                    "$port=$rule | Get-NetFirewallPortFilter -ErrorAction SilentlyContinue; " +
                    "$address=$rule | Get-NetFirewallAddressFilter -ErrorAction SilentlyContinue; " +
                    "[pscustomobject]@{Name=$rule.Name;DisplayName=$rule.DisplayName;Direction=$rule.Direction.ToString();" +
                    "Action=$rule.Action.ToString();Profile=$rule.Profile.ToString();Program=$app.Program;" +
                    "Protocol=$port.Protocol;LocalPort=$port.LocalPort;RemotePort=$port.RemotePort;" +
                    "RemoteAddress=$address.RemoteAddress} } | ConvertTo-Json -Compress";

                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoLogo -NoProfile -NonInteractive -Command \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                if (!process.Start())
                {
                    return Empty("Firewall-rule data was unavailable.");
                }

                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(12000) || process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    TryKill(process);
                    return Empty("Firewall-rule data was unavailable.");
                }

                using JsonDocument document = JsonDocument.Parse(output);
                IEnumerable<JsonElement> items = document.RootElement.ValueKind == JsonValueKind.Array
                    ? document.RootElement.EnumerateArray()
                    : new[] { document.RootElement };

                List<FirewallRuleFinding> findings = new();
                int enabledRuleCount = 0;
                int inboundAllowRuleCount = 0;

                foreach (JsonElement item in items)
                {
                    enabledRuleCount++;

                    string name = GetString(item, "DisplayName");
                    string direction = GetString(item, "Direction");
                    string action = GetString(item, "Action");
                    string profile = GetString(item, "Profile");
                    string program = GetString(item, "Program");
                    string protocol = GetValue(item, "Protocol");
                    string localPort = GetValue(item, "LocalPort");
                    string remoteAddress = GetValue(item, "RemoteAddress");

                    bool inboundAllow =
                        direction.Equals("Inbound", StringComparison.OrdinalIgnoreCase) &&
                        action.Equals("Allow", StringComparison.OrdinalIgnoreCase);

                    if (!inboundAllow)
                    {
                        continue;
                    }

                    inboundAllowRuleCount++;

                    bool allProfiles =
                        profile.Contains("Any", StringComparison.OrdinalIgnoreCase) ||
                        profile.Contains("Domain, Private, Public", StringComparison.OrdinalIgnoreCase);
                    bool anyProgram = IsAny(program);
                    bool broadPort = IsAny(localPort);
                    bool anyRemoteAddress = IsAny(remoteAddress);

                    if (allProfiles && anyProgram && broadPort && anyRemoteAddress)
                    {
                        findings.Add(new FirewallRuleFinding(
                            Fallback(name, "Unnamed firewall rule"),
                            "An enabled inbound allow rule applies to all programs, ports, remote addresses, and network profiles."));
                    }
                    else if (allProfiles && anyProgram && anyRemoteAddress && IsHighRiskPort(localPort))
                    {
                        findings.Add(new FirewallRuleFinding(
                            Fallback(name, "Unnamed firewall rule"),
                            $"An enabled inbound allow rule exposes {protocol} port {localPort} to any remote address on all profiles."));
                    }
                }

                FirewallRuleFinding? primary = findings.Count > 0 ? findings[0] : null;
                return new FirewallRuleSnapshot(
                    enabledRuleCount,
                    inboundAllowRuleCount,
                    findings.Count,
                    primary?.RuleName ?? "None",
                    primary?.Reason ?? "No unusually broad enabled firewall rules were detected.");
            }
            catch
            {
                return Empty("Firewall-rule data was unavailable.");
            }
        }

        private static bool IsHighRiskPort(string value)
        {
            string normalized = value.Trim();
            return normalized.Equals("3389", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("445", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("135", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("5985", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("5986", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAny(string value) =>
            string.IsNullOrWhiteSpace(value) ||
            value.Equals("Any", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("*", StringComparison.OrdinalIgnoreCase);

        private static string GetString(JsonElement item, string propertyName)
        {
            return item.TryGetProperty(propertyName, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static string GetValue(JsonElement item, string propertyName)
        {
            if (!item.TryGetProperty(propertyName, out JsonElement value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return string.Empty;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }

        private static string Fallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private static FirewallRuleSnapshot Empty(string reason) =>
            new(0, 0, 0, "None", reason);

        private sealed record FirewallRuleFinding(string RuleName, string Reason);

        public sealed record FirewallRuleSnapshot(
            int EnabledRuleCount,
            int InboundAllowRuleCount,
            int ReviewFindingCount,
            string PrimaryRuleName,
            string PrimaryReason);
    }
}
