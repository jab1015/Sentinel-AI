/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Applies remediation policy and explicit approval before delegating to the
    /// bounded, idempotent, rollback-capable firewall containment transaction.
    /// </summary>
    public sealed class FirewallRemediationService
    {
        private readonly RemediationPolicy _policy;
        private readonly FirewallContainmentService _containmentService;

        public FirewallRemediationService(
            RemediationPolicy? policy = null,
            FirewallContainmentService? containmentService = null)
        {
            _policy = policy ?? new RemediationPolicy();
            _containmentService = containmentService ?? new FirewallContainmentService();
        }

        public async Task<FirewallRemediationResult> BlockRemoteAddressAsync(
            string remoteAddress,
            bool hasVerifiedEvidence,
            bool isWindowsProtectedComponent,
            bool userApproved,
            bool canRequestElevation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IPAddress.TryParse(remoteAddress, out IPAddress? parsedAddress))
                return Failed("Sentinel could not verify a valid network address to block.");

            string normalizedAddress = parsedAddress.ToString();
            RemediationPolicy.RemediationDecision decision = _policy.Evaluate(
                new RemediationPolicy.RemediationRequest(
                    RemediationPolicy.RemediationAction.BlockNetworkEndpoint,
                    RemediationPolicy.RemediationRisk.Moderate,
                    hasVerifiedEvidence,
                    isWindowsProtectedComponent,
                    RequiresElevation: true,
                    CanRequestElevation: canRequestElevation));

            if (!decision.Allowed)
                return Failed(decision.Explanation);

            if (decision.RequiresUserApproval && !userApproved)
                return new FirewallRemediationResult(false, true, false, decision.Explanation);

            cancellationToken.ThrowIfCancellationRequested();
            FirewallContainmentService.FirewallContainmentResult containment =
                await _containmentService.BlockEndpointAsync(normalizedAddress)
                    .ConfigureAwait(false);

            bool verified =
                containment.Succeeded &&
                !containment.RolledBack &&
                containment.ConnectivityHealthy;

            return new FirewallRemediationResult(
                Succeeded: verified,
                RequiresUserApproval: false,
                RuleVerified: verified,
                Message: verified
                    ? containment.Summary
                    : containment.RolledBack
                        ? "Sentinel attempted the approved firewall block, but verification did not pass and the new rule was rolled back."
                        : containment.Summary);
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
