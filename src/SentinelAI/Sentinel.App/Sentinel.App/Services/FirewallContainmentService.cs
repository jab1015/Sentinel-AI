/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Creates and verifies a narrowly-scoped outbound Windows Firewall block for a
    /// specific remote IP address. This service performs no threat classification;
    /// callers must obtain policy approval before invoking it.
    /// </summary>
    public sealed class FirewallContainmentService
    {
        private const string RulePrefix = "Sentinel AI Block";

        public async Task<FirewallContainmentResult> BlockEndpointAsync(string remoteEndpoint)
        {
            if (!TryExtractRemoteAddress(remoteEndpoint, out IPAddress? address) || address is null)
            {
                return FirewallContainmentResult.Failure(
                    "Containment target is invalid",
                    "Sentinel could not identify a valid remote IP address to block.");
            }

            string remoteIp = address.ToString();
            string ruleName = BuildRuleName(remoteIp);

            try
            {
                int addExitCode = await RunNetshElevatedAsync(
                    $"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block remoteip={remoteIp} enable=yes profile=any");

                if (addExitCode != 0)
                {
                    return FirewallContainmentResult.Failure(
                        "Network block was not created",
                        $"Windows Firewall returned exit code {addExitCode}. No successful containment claim was recorded.");
                }

                bool verified = await VerifyRuleAsync(ruleName, remoteIp);
                if (!verified)
                {
                    return FirewallContainmentResult.Failure(
                        "Network block could not be verified",
                        "Windows reported that the firewall command completed, but Sentinel could not verify the expected outbound block rule.");
                }

                return new FirewallContainmentResult(
                    Attempted: true,
                    Succeeded: true,
                    RuleName: ruleName,
                    RemoteIp: remoteIp,
                    Title: "Suspicious network destination blocked",
                    Summary: $"Sentinel created and verified a Windows Firewall outbound block for {remoteIp}.");
            }
            catch (Exception ex)
            {
                return FirewallContainmentResult.Failure(
                    "Network containment could not complete",
                    $"Sentinel did not report the endpoint as blocked because verification did not complete. {ex.Message}");
            }
        }

        public async Task<FirewallContainmentResult> RemoveBlockAsync(string remoteEndpoint)
        {
            if (!TryExtractRemoteAddress(remoteEndpoint, out IPAddress? address) || address is null)
            {
                return FirewallContainmentResult.Failure(
                    "Containment target is invalid",
                    "Sentinel could not identify the remote IP address for this block.");
            }

            string remoteIp = address.ToString();
            string ruleName = BuildRuleName(remoteIp);

            try
            {
                int deleteExitCode = await RunNetshElevatedAsync(
                    $"advfirewall firewall delete rule name=\"{ruleName}\"");

                bool stillExists = await VerifyRuleAsync(ruleName, remoteIp);
                if (deleteExitCode != 0 || stillExists)
                {
                    return FirewallContainmentResult.Failure(
                        "Network block could not be removed",
                        "Sentinel could not verify removal of the Windows Firewall rule.");
                }

                return new FirewallContainmentResult(
                    Attempted: true,
                    Succeeded: true,
                    RuleName: ruleName,
                    RemoteIp: remoteIp,
                    Title: "Network block removed",
                    Summary: $"Sentinel removed and verified removal of the Windows Firewall block for {remoteIp}.");
            }
            catch (Exception ex)
            {
                return FirewallContainmentResult.Failure(
                    "Network block could not be removed",
                    $"Sentinel could not verify removal of the firewall rule. {ex.Message}");
            }
        }

        private static async Task<int> RunNetshElevatedAsync(string arguments)
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            process.Start();
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }

        private static async Task<bool> VerifyRuleAsync(string ruleName, string remoteIp)
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"advfirewall firewall show rule name=\"{ruleName}\" verbose",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            return process.ExitCode == 0 &&
                   output.Contains(ruleName, StringComparison.OrdinalIgnoreCase) &&
                   output.Contains(remoteIp, StringComparison.OrdinalIgnoreCase) &&
                   output.Contains("Block", StringComparison.OrdinalIgnoreCase) &&
                   output.Contains("Out", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryExtractRemoteAddress(string value, out IPAddress? address)
        {
            address = null;
            if (string.IsNullOrWhiteSpace(value)) return false;

            string candidate = value.Trim();
            if (IPAddress.TryParse(candidate.Trim('[', ']'), out address)) return true;

            int separator = candidate.LastIndexOf(':');
            if (separator <= 0) return false;

            candidate = candidate[..separator].Trim('[', ']');
            return IPAddress.TryParse(candidate, out address);
        }

        private static string BuildRuleName(string remoteIp)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(remoteIp));
            string suffix = Convert.ToHexString(hash)[..12];
            return $"{RulePrefix} {suffix}";
        }

        public sealed record FirewallContainmentResult(
            bool Attempted,
            bool Succeeded,
            string RuleName,
            string RemoteIp,
            string Title,
            string Summary)
        {
            public static FirewallContainmentResult Failure(string title, string summary) =>
                new(true, false, string.Empty, string.Empty, title, summary);
        }
    }
}
