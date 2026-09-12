/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    public sealed class FirewallContainmentService
    {
        private const string RulePrefix = "Sentinel AI Block";
        private static readonly SemaphoreSlim ContainmentGate = new(1, 1);
        private readonly PrivilegedBrokerClient _brokerClient = new();

        public async Task<FirewallContainmentResult> BlockEndpointAsync(string remoteEndpoint)
        {
            await ContainmentGate.WaitAsync().ConfigureAwait(false);
            try { return await BlockEndpointCoreAsync(remoteEndpoint).ConfigureAwait(false); }
            finally { ContainmentGate.Release(); }
        }

        private async Task<FirewallContainmentResult> BlockEndpointCoreAsync(string remoteEndpoint)
        {
            if (!TryExtractRemoteAddress(remoteEndpoint, out IPAddress? address) || address is null)
                return FirewallContainmentResult.Failure("Containment target is invalid", "Sentinel could not identify a valid remote IP address to block.");

            string remoteIp = address.ToString();
            string ruleName = BuildRuleName(remoteIp);
            ConnectivityState before = await CheckConnectivityAsync().ConfigureAwait(false);
            bool ruleCreated = false;

            try
            {
                FirewallRuleVerification existing = await QueryAndVerifyRuleAsync(ruleName, remoteIp).ConfigureAwait(false);
                if (!existing.QueryValid)
                {
                    return FirewallContainmentResult.Failure(
                        "Firewall state could not be verified",
                        $"Sentinel could not safely determine whether its deterministic firewall rule already exists ({existing.Detail}). No firewall change was made.");
                }

                if (existing.Exists)
                {
                    return existing.IsExactBlock
                        ? new FirewallContainmentResult(false, true, ruleName, remoteIp,
                            "Network destination already contained",
                            $"Sentinel verified an enabled outbound Windows Firewall Block rule for {remoteIp} with the exact expected address/program/port scope.",
                            false, before.IsHealthy, FirewallContainmentOutcome.Successful)
                        : new FirewallContainmentResult(false, false, ruleName, remoteIp,
                            "Existing firewall rule conflicts with containment",
                            $"A rule named {ruleName} already exists but does not exactly match Sentinel's required enabled outbound Block scope. Sentinel made no firewall change.",
                            false, before.IsHealthy, FirewallContainmentOutcome.RuleConflict);
                }

                BrokerInvocationResult mutation = await _brokerClient.BlockFirewallEndpointAsync(remoteIp).ConfigureAwait(false);
                if (!mutation.Succeeded)
                    return FirewallContainmentResult.Failure("Network block was not created", $"The privileged broker refused or failed the firewall mutation ({mutation.Code}). No successful containment claim was recorded.");

                ruleCreated = true;
                FirewallRuleVerification verified = await QueryAndVerifyRuleAsync(ruleName, remoteIp).ConfigureAwait(false);
                if (!verified.QueryValid || !verified.IsExactBlock)
                {
                    FirewallContainmentResult cleanup = await RemoveBlockCoreAsync(remoteEndpoint).ConfigureAwait(false);
                    return cleanup.Succeeded
                        ? new FirewallContainmentResult(true, false, ruleName, remoteIp,
                            "Unverified network block removed",
                            $"Windows created a rule, but exact enforcement verification failed ({verified.Detail}). Sentinel removed it and verified cleanup.",
                            true, cleanup.ConnectivityHealthy, FirewallContainmentOutcome.VerificationFailed)
                        : new FirewallContainmentResult(true, false, ruleName, remoteIp,
                            "Unverified network block requires review",
                            $"Windows created a rule, but exact enforcement verification failed ({verified.Detail}) and cleanup could not be verified.",
                            false, false, FirewallContainmentOutcome.PartialContainment);
                }

                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                ConnectivityState after = await CheckConnectivityAsync().ConfigureAwait(false);
                if (before.IsHealthy && !after.IsHealthy)
                {
                    FirewallContainmentResult rollback = await RemoveBlockCoreAsync(remoteEndpoint).ConfigureAwait(false);
                    return rollback.Succeeded
                        ? new FirewallContainmentResult(true, false, ruleName, remoteIp,
                            "Network block automatically undone",
                            "Connectivity became unavailable immediately after containment. Sentinel removed the rule and verified rollback.",
                            true, false, FirewallContainmentOutcome.VerificationFailed)
                        : new FirewallContainmentResult(true, false, ruleName, remoteIp,
                            "Network block may have affected connectivity",
                            "Connectivity was lost and Sentinel could not verify rule removal. Windows Firewall requires review.",
                            false, false, FirewallContainmentOutcome.PartialContainment);
                }

                return new FirewallContainmentResult(true, true, ruleName, remoteIp,
                    "Suspicious network destination blocked",
                    $"Sentinel created and verified an enabled outbound Windows Firewall Block rule for {remoteIp}. Exact address, program/service, profile, protocol and port scope were re-read from the active firewall policy store.",
                    false, after.IsHealthy || !before.IsHealthy, FirewallContainmentOutcome.Successful);
            }
            catch (Exception ex)
            {
                if (ruleCreated)
                {
                    FirewallContainmentResult cleanup = await RemoveBlockCoreAsync(remoteEndpoint).ConfigureAwait(false);
                    if (cleanup.Succeeded)
                        return new FirewallContainmentResult(true, false, ruleName, remoteIp,
                            "Unverified network block removed",
                            $"Containment verification stopped unexpectedly ({ex.GetType().Name}). Sentinel removed the new rule and verified rollback.",
                            true, cleanup.ConnectivityHealthy, FirewallContainmentOutcome.VerificationFailed);
                }

                return FirewallContainmentResult.Failure("Network containment could not complete",
                    $"Sentinel did not report the endpoint as blocked because execution did not complete ({ex.GetType().Name}).");
            }
        }

        public async Task<FirewallContainmentResult> RemoveBlockAsync(string remoteEndpoint)
        {
            await ContainmentGate.WaitAsync().ConfigureAwait(false);
            try { return await RemoveBlockCoreAsync(remoteEndpoint).ConfigureAwait(false); }
            finally { ContainmentGate.Release(); }
        }

        private async Task<FirewallContainmentResult> RemoveBlockCoreAsync(string remoteEndpoint)
        {
            if (!TryExtractRemoteAddress(remoteEndpoint, out IPAddress? address) || address is null)
                return FirewallContainmentResult.Failure("Containment target is invalid", "Sentinel could not identify the remote IP address for this block.");

            string remoteIp = address.ToString();
            string ruleName = BuildRuleName(remoteIp);
            try
            {
                FirewallRuleVerification existing = await QueryAndVerifyRuleAsync(ruleName, remoteIp).ConfigureAwait(false);
                if (!existing.QueryValid)
                {
                    return FirewallContainmentResult.Failure(
                        "Firewall state could not be verified",
                        $"Sentinel could not safely verify the firewall rule before removal ({existing.Detail}). No firewall change was made.");
                }

                if (!existing.Exists)
                {
                    ConnectivityState alreadyAbsentConnectivity = await CheckConnectivityAsync().ConfigureAwait(false);
                    return new FirewallContainmentResult(false, true, ruleName, remoteIp,
                        "Network block already absent",
                        $"Sentinel verified that its expected Windows Firewall block for {remoteIp} is not present. No firewall change was needed.",
                        false, alreadyAbsentConnectivity.IsHealthy, FirewallContainmentOutcome.Successful);
                }

                if (!existing.IsExactBlock)
                {
                    return new FirewallContainmentResult(false, false, ruleName, remoteIp,
                        "Conflicting firewall rule was not removed",
                        $"A rule named {ruleName} exists but does not exactly match Sentinel's required enabled outbound Block scope. Sentinel refused to delete it.",
                        false, false, FirewallContainmentOutcome.RuleConflict);
                }

                BrokerInvocationResult mutation = await _brokerClient.RemoveFirewallEndpointAsync(remoteIp).ConfigureAwait(false);
                FirewallRuleVerification remaining = await QueryAndVerifyRuleAsync(ruleName, remoteIp).ConfigureAwait(false);
                if (!mutation.Succeeded || !remaining.QueryValid || remaining.Exists)
                {
                    string detail = !remaining.QueryValid
                        ? $" Firewall verification failed closed ({remaining.Detail})."
                        : string.Empty;
                    return FirewallContainmentResult.Failure("Network block could not be removed", "Sentinel could not verify removal of the Windows Firewall rule." + detail);
                }

                ConnectivityState connectivity = await CheckConnectivityAsync().ConfigureAwait(false);
                return new FirewallContainmentResult(true, true, ruleName, remoteIp,
                    "Network block removed", $"Sentinel removed and verified removal of the Windows Firewall block for {remoteIp}.",
                    true, connectivity.IsHealthy, FirewallContainmentOutcome.Successful);
            }
            catch (Exception ex)
            {
                return FirewallContainmentResult.Failure("Network block could not be removed", $"Sentinel could not verify removal of the firewall rule ({ex.GetType().Name}).");
            }
        }

        private static async Task<FirewallRuleVerification> QueryAndVerifyRuleAsync(string ruleName, string remoteIp)
        {
            string safeName = EscapePowerShellLiteral(ruleName);
            string command =
                "$ErrorActionPreference='Stop'; try { " +
                "$name='" + safeName + "'; " +
                "$rules=@(Get-NetFirewallRule -PolicyStore ActiveStore -DisplayName $name -ErrorAction Stop | Where-Object {$_.DisplayName -eq $name}); " +
                "if($rules.Count -eq 0){'FOUND=0'; exit 0}; if($rules.Count -ne 1){\"FOUND=$($rules.Count)`nCONFLICT=True\"; exit 0}; " +
                "$r=$rules[0]; $a=@($r | Get-NetFirewallAddressFilter -ErrorAction Stop); $p=@($r | Get-NetFirewallPortFilter -ErrorAction Stop); $app=@($r | Get-NetFirewallApplicationFilter -ErrorAction Stop); $svc=@($r | Get-NetFirewallServiceFilter -ErrorAction Stop); " +
                "\"FOUND=1`nENABLED=$($r.Enabled)`nACTION=$($r.Action)`nDIRECTION=$($r.Direction)`nPROFILE=$($r.Profile)`nREMOTE=$(@($a.RemoteAddress) -join ',')`nLOCAL=$(@($a.LocalAddress) -join ',')`nPROTOCOL=$($p.Protocol)`nLOCALPORT=$(@($p.LocalPort) -join ',')`nREMOTEPORT=$(@($p.RemotePort) -join ',')`nPROGRAM=$($app.Program)`nSERVICE=$($svc.Service)\"; " +
                "} catch { Write-Error 'SENTINEL_FIREWALL_QUERY_FAILED'; exit 70 }";

            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            ProcessStartInfo startInfo = new()
            {
                FileName = ResolvePowerShellPath(),
                Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            ProcessExecutionResult result = await BoundedProcessRunner.RunAsync(startInfo, TimeSpan.FromSeconds(12)).ConfigureAwait(false);
            if (!result.Succeeded)
                return new(false, false, false, $"firewall query failed: {result.Outcome}");

            if (!TryParseKeyValues(result.StandardOutput, out Dictionary<string, string> values))
                return new(false, false, false, "firewall query output was malformed or ambiguous");
            return FirewallRuleVerificationPolicy.Evaluate(values, remoteIp);
        }

        private static bool TryParseKeyValues(string output, out Dictionary<string, string> values)
        {
            values = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in (output ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0) return false;
                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                if (key.Length == 0 || values.ContainsKey(key)) return false;
                values.Add(key, value);
            }
            return true;
        }

        private static async Task<ConnectivityState> CheckConnectivityAsync()
        {
            if (!NetworkInterface.GetIsNetworkAvailable()) return new(false);
            bool dnsOk = false, tcpOk = false;
            try { dnsOk = (await Dns.GetHostAddressesAsync("www.microsoft.com").WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false)).Length > 0; } catch { }
            try
            {
                using TcpClient client = new();
                await client.ConnectAsync("www.microsoft.com", 443).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                tcpOk = client.Connected;
            }
            catch { }
            return new(dnsOk && tcpOk);
        }

        private static string ResolvePowerShellPath()
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return string.IsNullOrWhiteSpace(system) ? "powershell.exe" : Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
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
            return $"{RulePrefix} {Convert.ToHexString(hash)[..12]}";
        }

        private static string EscapePowerShellLiteral(string value) => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

        private sealed record ConnectivityState(bool IsHealthy);

        public enum FirewallContainmentOutcome
        {
            Successful,
            PartialContainment,
            VerificationFailed,
            OperationFailed,
            RuleConflict
        }

        public sealed record FirewallContainmentResult(
            bool Attempted,
            bool Succeeded,
            string RuleName,
            string RemoteIp,
            string Title,
            string Summary,
            bool RolledBack = false,
            bool ConnectivityHealthy = true,
            FirewallContainmentOutcome Outcome = FirewallContainmentOutcome.OperationFailed)
        {
            public static FirewallContainmentResult Failure(string title, string summary) =>
                new(true, false, string.Empty, string.Empty, title, summary, false, false, FirewallContainmentOutcome.OperationFailed);
        }
    }
}
