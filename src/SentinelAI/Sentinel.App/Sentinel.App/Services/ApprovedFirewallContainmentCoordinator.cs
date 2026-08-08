/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Connects a short-lived exact-target remediation approval to the verified
    /// Windows Firewall containment service. The current investigation must still
    /// match the approved action and target immediately before execution.
    /// </summary>
    public sealed class ApprovedFirewallContainmentCoordinator
    {
        private readonly ApprovedRemediationExecutor _approvedExecutor;
        private readonly FirewallContainmentService _firewallContainment;
        private readonly StoreSubscriptionService _subscriptionService;

        public ApprovedFirewallContainmentCoordinator(
            ApprovedRemediationExecutor? approvedExecutor = null,
            FirewallContainmentService? firewallContainment = null,
            StoreSubscriptionService? subscriptionService = null)
        {
            _approvedExecutor = approvedExecutor ?? new ApprovedRemediationExecutor();
            _firewallContainment = firewallContainment ?? new FirewallContainmentService();
            _subscriptionService = subscriptionService ?? new StoreSubscriptionService();
        }

        public async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation)
        {
            ArgumentNullException.ThrowIfNull(currentSnapshot);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(validation);

            SubscriptionState subscription = await _subscriptionService.GetStateAsync().ConfigureAwait(false);
            if (!subscription.IsActive)
            {
                return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                    "Free local network monitoring remains active, but an active Sentinel AI subscription is required for firewall containment.");
            }

            if (!string.Equals(request.Action, "block-outbound-endpoint", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                    "This approval is not for outbound endpoint containment. Sentinel made no system change.");
            }

            FirewallContainmentService.FirewallContainmentResult? containmentResult = null;

            return await _approvedExecutor.ExecuteAsync(
                currentSnapshot,
                request,
                validation,
                executeAsync: async () =>
                {
                    containmentResult = await _firewallContainment
                        .BlockEndpointAsync(request.Target)
                        .ConfigureAwait(false);

                    if (!containmentResult.Succeeded)
                    {
                        throw new InvalidOperationException(containmentResult.Summary);
                    }
                },
                verifyAsync: () => Task.FromResult(
                    containmentResult is
                    {
                        Succeeded: true,
                        RolledBack: false,
                        ConnectivityHealthy: true
                    }))
                .ConfigureAwait(false);
        }

        public async Task<FirewallContainmentService.FirewallContainmentResult> RemoveBlockAsync(
            string remoteEndpoint)
        {
            SubscriptionState subscription = await _subscriptionService.GetStateAsync().ConfigureAwait(false);
            if (!subscription.IsActive)
            {
                return FirewallContainmentService.FirewallContainmentResult.Failure(
                    "Subscription required",
                    "An active Sentinel AI subscription is required before changing a containment rule.");
            }

            if (string.IsNullOrWhiteSpace(remoteEndpoint))
            {
                return FirewallContainmentService.FirewallContainmentResult.Failure(
                    "Containment target is invalid",
                    "Sentinel could not identify the network block to remove.");
            }

            return await _firewallContainment
                .RemoveBlockAsync(remoteEndpoint)
                .ConfigureAwait(false);
        }
    }
}
