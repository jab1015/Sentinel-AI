/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Creates and verifies a narrowly-scoped outbound Windows Firewall block for a
    /// specific remote IP address. This service performs no threat classification;
    /// callers must obtain policy approval before invoking it. If Sentinel detects
    /// that connectivity was healthy before containment and materially degraded after
    /// the new rule, the rule is automatically removed and the rollback is verified.
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
            ConnectivityState before = await CheckConnectivityAsync().ConfigureAwait(false);

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

                bool verified = await VerifyRuleAsync(ruleName, remoteIp).ConfigureAwait(false);
                if (!verified)
                {
                    FirewallContainmentResult cleanup = await RemoveBlockAsync(remoteEndpoint).ConfigureAwait(false);
                    return cleanup.Succeeded
                        ? new FirewallContainmentResult(
                            Attempted: true,
                            Succeeded: false,
                            RuleName: ruleName,
                            RemoteIp: remoteIp,
                            Title: "Unverified network block removed",
                            Summary: "Windows created a firewall rule, but Sentinel could not verify every expected property. Sentinel removed the new rule and verified cleanup instead of leaving an unverified system change in place.",
                            RolledBack: true,
                            ConnectivityHealthy: cleanup.ConnectivityHealthy)
                        : new FirewallContainmentResult(
                            Attempted: true,
                            Succeeded: false,
                            RuleName: ruleName,
                            RemoteIp: remoteIp,
                            Title: "Unverified network block requires review",
                            Summary: "Windows created a firewall rule, but Sentinel could not verify it or verify cleanup. Review Windows Firewall rules before relying on the containment result.",
                            RolledBack: false,
                            ConnectivityHealthy: false);
                }

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                ConnectivityState after = await CheckConnectivityAsync().ConfigureAwait(false);

                if (before.IsHealthy && !after.IsHealthy)
                {
                    FirewallContainmentResult rollback = await RemoveBlockAsync(remoteEndpoint).ConfigureAwait(false);
                    return rollback.Succeeded
                        ? new FirewallContainmentResult(
                            Attempted: true,
                            Succeeded: false,
                            RuleName: ruleName,
                            RemoteIp: remoteIp,
                            Title: "Network block automatically undone",
                            Summary: "Sentinel detected that internet connectivity became unavailable immediately after the block. The new firewall rule was automatically removed and the rollback was verified. Sentinel will continue investigating instead of leaving the computer offline.",
                            RolledBack: true,
                            ConnectivityHealthy: false)
                        : new FirewallContainmentResult(
                            Attempted: true,
                            Succeeded: false,
                            RuleName: ruleName,
                            RemoteIp: remoteIp,
                            Title: "Network block may have affected connectivity",
                            Summary: "Sentinel detected a loss of connectivity after containment and attempted to undo the block, but removal could not be verified. User assistance is required to review Windows Firewall rules.",
                            RolledBack: false,
                            ConnectivityHealthy: false);
                }

                return new FirewallContainmentResult(
                    Attempted: true,
                    Succeeded: true,
                    RuleName: ruleName,
                    RemoteIp: remoteIp,
                    Title: "Suspicious network destination blocked",
                    Summary: $"Sentinel created and verified a Windows Firewall outbound block for {remoteIp}. Connectivity remained available after containment.",
                    RolledBack: false,
                    ConnectivityHealthy: after.IsHealthy || !before.IsHealthy);
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

                bool stillExists = await RuleExistsAsync(ruleName).ConfigureAwait(false);
                if (deleteExitCode != 0 || stillExists)
                {
                    return FirewallContainmentResult.Failure(
                        "Network block could not be removed",
                        "Sentinel could not verify removal of the Windows Firewall rule.");
                }

                ConnectivityState connectivity = await CheckConnectivityAsync().ConfigureAwait(false);
                return new FirewallContainmentResult(
                    Attempted: true,
                    Succeeded: true,
                    RuleName: ruleName,
                    RemoteIp: remoteIp,
                    Title: "Network block removed",
                    Summary: $"Sentinel removed and verified removal of the Windows Firewall block for {remoteIp}.",
                    RolledBack: true,
                    ConnectivityHealthy: connectivity.IsHealthy);
            }
            catch (Exception ex)
            {
                return FirewallContainmentResult.Failure(
                    "Network block could not be removed",
                    $"Sentinel could not verify removal of the firewall rule. {ex.Message}");
            }
        }

        private static async Task<ConnectivityState> CheckConnectivityAsync()
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return new ConnectivityState(false, "Windows reports no active network connection.");
            }

            bool dnsOk = false;
            bool tcpOk = false;

            try
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync("www.microsoft.com")
                    .WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                dnsOk = addresses.Length > 0;
            }
            catch { }

            try
            {
                using TcpClient client = new();
                await client.ConnectAsync("www.microsoft.com", 443)
                    .WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                tcpOk = client.Connected;
            }
            catch { }

            return new ConnectivityState(
                dnsOk && tcpOk,
                $"DNS={(dnsOk ? "available" : "unavailable")}; HTTPS={(tcpOk ? "available" : "unavailable")}");
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

        private static async Task<bool> RuleExistsAsync(string ruleName)
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            Task<string> outputRead = process.StandardOutput.ReadToEndAsync();
            Task<string> errorRead = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            string output = await outputRead.ConfigureAwait(false);
            _ = await errorRead.ConfigureAwait(false);

            return process.ExitCode == 0 &&
                   output.Contains(ruleName, StringComparison.OrdinalIgnoreCase);
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

        private sealed record ConnectivityState(bool IsHealthy, string Evidence);

        public sealed record FirewallContainmentResult(
            bool Attempted,
            bool Succeeded,
            string RuleName,
            string RemoteIp,
            string Title,
            string Summary,
            bool RolledBack = false,
            bool ConnectivityHealthy = true)
        {
            public static FirewallContainmentResult Failure(string title, string summary) =>
                new(true, false, string.Empty, string.Empty, title, summary, false, false);
        }
    }
}
