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

                int addExitCode = await RunNetshElevatedAsync(
                    $"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block remoteip={remoteIp} enable=yes profile=any").ConfigureAwait(false);

                if (addExitCode != 0)
                    return FirewallContainmentResult.Failure("Network block was not created", $"Windows Firewall returned exit code {addExitCode}. No successful containment claim was recorded.");

                ruleCreated = true;
                FirewallRuleVerification verified = await QueryAndVerifyRuleAsync(ruleName, remoteIp).ConfigureAwait(false);
                if (!verified.IsExactBlock)
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
                int deleteExitCode = await RunNetshElevatedAsync($"advfirewall firewall delete rule name=\"{ruleName}\"").ConfigureAwait(false);
                FirewallRuleVerification remaining = await QueryAndVerifyRuleAsync(ruleName, remoteIp).ConfigureAwait(false);
                if (deleteExitCode != 0 || remaining.Exists)
                    return FirewallContainmentResult.Failure("Network block could not be removed", "Sentinel could not verify removal of the Windows Firewall rule.");

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
                "$name='" + safeName + "'; " +
                "$rules=@(Get-NetFirewallRule -PolicyStore ActiveStore -DisplayName $name -ErrorAction SilentlyContinue | Where-Object {$_.DisplayName -eq $name}); " +
                "if($rules.Count -eq 0){'FOUND=0'; exit 0}; if($rules.Count -ne 1){\"FOUND=$($rules.Count)`nCONFLICT=True\"; exit 0}; " +
                "$r=$rules[0]; $a=@($r | Get-NetFirewallAddressFilter); $p=@($r | Get-NetFirewallPortFilter); $app=@($r | Get-NetFirewallApplicationFilter); $svc=@($r | Get-NetFirewallServiceFilter); " +
                "\"FOUND=1`nENABLED=$($r.Enabled)`nACTION=$($r.Action)`nDIRECTION=$($r.Direction)`nPROFILE=$($r.Profile)`nREMOTE=$(@($a.RemoteAddress) -join ',')`nLOCAL=$(@($a.LocalAddress) -join ',')`nPROTOCOL=$($p.Protocol)`nLOCALPORT=$(@($p.LocalPort) -join ',')`nREMOTEPORT=$(@($p.RemotePort) -join ',')`nPROGRAM=$($app.Program)`nSERVICE=$($svc.Service)\"";

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
                return new(false, false, $"firewall query failed: {result.Outcome}");

            Dictionary<string, string> values = ParseKeyValues(result.StandardOutput);
            if (!values.TryGetValue("FOUND", out string? foundText) || !int.TryParse(foundText, out int found) || found == 0)
                return new(false, false, "rule not found");
            if (found != 1 || values.ContainsKey("CONFLICT"))
                return new(true, false, "more than one rule has the deterministic Sentinel name");

            bool enabled = values.TryGetValue("ENABLED", out string? enabledText) && enabledText.Equals("True", StringComparison.OrdinalIgnoreCase);
            bool block = values.TryGetValue("ACTION", out string? action) && action.Equals("Block", StringComparison.OrdinalIgnoreCase);
            bool outbound = values.TryGetValue("DIRECTION", out string? direction) && (direction.Equals("Outbound", StringComparison.OrdinalIgnoreCase) || direction.Equals("Out", StringComparison.OrdinalIgnoreCase));
            bool profileAny = values.TryGetValue("PROFILE", out string? profile) && profile.Equals("Any", StringComparison.OrdinalIgnoreCase);
            bool remoteExact = values.TryGetValue("REMOTE", out string? remote) && AddressListExactlyMatches(remote, remoteIp);
            bool localAny = values.TryGetValue("LOCAL", out string? local) && IsAny(local);
            bool protocolAny = values.TryGetValue("PROTOCOL", out string? protocol) && IsAny(protocol);
            bool localPortAny = values.TryGetValue("LOCALPORT", out string? localPort) && IsAny(localPort);
            bool remotePortAny = values.TryGetValue("REMOTEPORT", out string? remotePort) && IsAny(remotePort);
            bool programAny = values.TryGetValue("PROGRAM", out string? program) && IsAny(program);
            bool serviceAny = values.TryGetValue("SERVICE", out string? service) && IsAny(service);

            bool exact = enabled && block && outbound && profileAny && remoteExact && localAny && protocolAny &&
                         localPortAny && remotePortAny && programAny && serviceAny;
            string detail = exact ? "exact enabled outbound Block rule verified" :
                $"Enabled={enabled}; ActionBlock={block}; Outbound={outbound}; ProfileAny={profileAny}; RemoteExact={remoteExact}; ProgramAny={programAny}; ServiceAny={serviceAny}; ProtocolAny={protocolAny}; PortsAny={localPortAny && remotePortAny}";
            return new(true, exact, detail);
        }

        private static bool AddressListExactlyMatches(string value, string expected)
        {
            string[] items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return items.Length == 1 && IPAddress.TryParse(items[0], out IPAddress? parsed) &&
                   parsed.ToString().Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAny(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            (value.Equals("Any", StringComparison.OrdinalIgnoreCase) || value.Equals("*", StringComparison.OrdinalIgnoreCase));

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

        private static async Task<int> RunNetshElevatedAsync(string arguments)
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ResolveSystemBinary("netsh.exe"),
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            process.Start();
            await WaitForExitBoundedAsync(process, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            return process.ExitCode;
        }

        private static async Task WaitForExitBoundedAsync(Process process, TimeSpan timeout)
        {
            try { await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
        }

        private static string ResolvePowerShellPath()
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return string.IsNullOrWhiteSpace(system) ? "powershell.exe" : Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        }

        private static string ResolveSystemBinary(string name)
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return string.IsNullOrWhiteSpace(system) ? name : Path.Combine(system, name);
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
        private sealed record FirewallRuleVerification(bool Exists, bool IsExactBlock, string Detail);

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
