/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Creates a narrowly scoped Windows Firewall outbound block rule only after
    /// Sentinel has verified the target and the user has approved the action.
    /// </summary>
    public sealed class FirewallRemediationService
    {
        private readonly RemediationPolicy _policy;

        public FirewallRemediationService(RemediationPolicy? policy = null)
        {
            _policy = policy ?? new RemediationPolicy();
        }

        public async Task<FirewallRemediationResult> BlockRemoteAddressAsync(
            string remoteAddress,
            bool hasVerifiedEvidence,
            bool isWindowsProtectedComponent,
            bool userApproved,
            bool canRequestElevation,
            CancellationToken cancellationToken = default)
        {
            if (!IPAddress.TryParse(remoteAddress, out var parsedAddress))
            {
                return Failed("Sentinel could not verify a valid network address to block.");
            }

            string normalizedAddress = parsedAddress.ToString();
            var decision = _policy.Evaluate(new RemediationPolicy.RemediationRequest(
                RemediationPolicy.RemediationAction.BlockNetworkEndpoint,
                RemediationPolicy.RemediationRisk.Moderate,
                hasVerifiedEvidence,
                isWindowsProtectedComponent,
                RequiresElevation: true,
                CanRequestElevation: canRequestElevation));

            if (!decision.Allowed)
            {
                return Failed(decision.Explanation);
            }

            if (decision.RequiresUserApproval && !userApproved)
            {
                return new FirewallRemediationResult(false, true, false, decision.Explanation);
            }

            string ruleName = $"Sentinel AI Block {normalizedAddress}";
            string arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block remoteip={normalizedAddress} enable=yes profile=any";

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return Failed("Sentinel could not start the Windows Firewall action.");
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    return Failed("Windows Firewall did not accept the requested block rule.");
                }

                bool verified = await VerifyRuleAsync(ruleName, cancellationToken).ConfigureAwait(false);
                return new FirewallRemediationResult(
                    Succeeded: verified,
                    RequiresUserApproval: false,
                    RuleVerified: verified,
                    Message: verified
                        ? $"Sentinel blocked outbound connections to {normalizedAddress} and verified the Windows Firewall rule."
                        : "Sentinel requested the firewall block, but could not verify the rule. It will not report the action as complete.");
            }
            catch (OperationCanceledException)
            {
                return Failed("The firewall action was canceled before Sentinel could verify the result.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                return Failed("Sentinel could not safely change Windows Firewall. No success was reported.");
            }
        }

        private static async Task<bool> VerifyRuleAsync(string ruleName, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return process.ExitCode == 0 &&
                   output.Contains(ruleName, StringComparison.OrdinalIgnoreCase) &&
                   output.Contains("Block", StringComparison.OrdinalIgnoreCase);
        }

        private static FirewallRemediationResult Failed(string message) =>
            new(false, false, false, message);

        public sealed record FirewallRemediationResult(
            bool Succeeded,
            bool RequiresUserApproval,
            bool RuleVerified,
            string Message);
    }
}
