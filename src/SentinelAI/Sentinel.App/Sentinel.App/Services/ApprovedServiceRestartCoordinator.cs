/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Connects Sentinel's short-lived approval workflow to concrete Windows
    /// remediation implementations. The exact action and target are rechecked
    /// before execution and success requires independent post-action verification.
    /// </summary>
    public sealed class ApprovedServiceRestartCoordinator
    {
        private readonly ApprovedRemediationExecutor _approvedExecutor;
        private readonly ServiceRemediationService _serviceRemediation;
        private readonly FirewallContainmentService _firewallContainment;
        private readonly ProcessContainmentService _processContainment;
        private readonly StoreSubscriptionService _subscriptionService;

        public ApprovedServiceRestartCoordinator(
            ApprovedRemediationExecutor? approvedExecutor = null,
            ServiceRemediationService? serviceRemediation = null,
            FirewallContainmentService? firewallContainment = null,
            ProcessContainmentService? processContainment = null,
            StoreSubscriptionService? subscriptionService = null)
        {
            _approvedExecutor = approvedExecutor ?? new ApprovedRemediationExecutor();
            _serviceRemediation = serviceRemediation ?? new ServiceRemediationService();
            _firewallContainment = firewallContainment ?? new FirewallContainmentService();
            _processContainment = processContainment ?? new ProcessContainmentService();
            _subscriptionService = subscriptionService ?? new StoreSubscriptionService();
        }

        public async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            bool canRequestElevation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(currentSnapshot);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(validation);

            SubscriptionState subscription = await _subscriptionService.GetStateAsync().ConfigureAwait(false);
            if (!subscription.IsActive)
            {
                return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                    "Free local monitoring remains active, but an active Sentinel AI subscription is required before repair or containment changes can be made.");
            }

            if (string.Equals(request.Action, "restart-service", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteServiceRestartAsync(
                    currentSnapshot,
                    request,
                    validation,
                    canRequestElevation,
                    cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(request.Action, "block-outbound-endpoint", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteNetworkBlockAsync(
                    currentSnapshot,
                    request,
                    validation,
                    canRequestElevation,
                    cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(request.Action, "contain-process", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteProcessContainmentAsync(
                    currentSnapshot,
                    request,
                    validation,
                    canRequestElevation,
                    cancellationToken).ConfigureAwait(false);
            }

            return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                "This approved remediation type does not yet have a verified execution workflow. Sentinel made no system change.");
        }

        private async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteServiceRestartAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            bool canRequestElevation,
            CancellationToken cancellationToken)
        {
            string serviceName = request.Target.Trim();
            ServiceRemediationService.ServiceRemediationResult? executionResult = null;

            return await _approvedExecutor.ExecuteAsync(
                currentSnapshot,
                request,
                validation,
                executeAsync: async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    executionResult = await _serviceRemediation.RestartAsync(
                        serviceName,
                        hasVerifiedEvidence: true,
                        isWindowsProtectedComponent: IsProtectedWindowsService(serviceName),
                        userApproved: true,
                        canRequestElevation: canRequestElevation,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    if (!executionResult.Succeeded)
                    {
                        throw new InvalidOperationException(executionResult.Message);
                    }
                },
                verifyAsync: async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (executionResult is null || !executionResult.Succeeded)
                    {
                        return false;
                    }

                    return await _serviceRemediation.IsRunningAsync(serviceName, cancellationToken)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false);
        }

        private async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteNetworkBlockAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            bool canRequestElevation,
            CancellationToken cancellationToken)
        {
            if (!canRequestElevation)
            {
                return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                    "Blocking this network destination requires administrator permission that Sentinel cannot currently request.");
            }

            string remoteEndpoint = request.Target.Trim();
            FirewallContainmentService.FirewallContainmentResult? executionResult = null;

            return await _approvedExecutor.ExecuteAsync(
                currentSnapshot,
                request,
                validation,
                executeAsync: async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    executionResult = await _firewallContainment.BlockEndpointAsync(remoteEndpoint)
                        .ConfigureAwait(false);

                    if (!executionResult.Succeeded)
                    {
                        throw new InvalidOperationException(executionResult.Summary);
                    }
                },
                verifyAsync: () => Task.FromResult(
                    executionResult is
                    {
                        Succeeded: true,
                        RolledBack: false,
                        ConnectivityHealthy: true
                    }),
                actionWasAttempted: () => executionResult?.Attempted == true,
                noActionSummary: () => executionResult?.Summary ?? "The approved network block was already present. No duplicate rule was created.")
                .ConfigureAwait(false);
        }

        private async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteProcessContainmentAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            bool canRequestElevation,
            CancellationToken cancellationToken)
        {
            if (!canRequestElevation)
            {
                return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                    "Containing this process requires administrator permission that Sentinel cannot currently request.");
            }

            string processName = request.Target.Trim();
            ProcessContainmentService.ProcessContainmentResult? executionResult = null;

            return await _approvedExecutor.ExecuteAsync(
                currentSnapshot,
                request,
                validation,
                executeAsync: async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (request.TargetProcessId <= 0 || !request.TargetProcessStartUtc.HasValue)
                    {
                        throw new InvalidOperationException(
                            "The approval did not include an exact process instance. Sentinel made no system change.");
                    }

                    executionResult = await _processContainment
                        .ContainAsync(
                            processName,
                            request.TargetProcessId,
                            request.TargetProcessStartUtc)
                        .ConfigureAwait(false);

                    if (!executionResult.Succeeded)
                    {
                        throw new InvalidOperationException(executionResult.Summary);
                    }
                },
                verifyAsync: () => Task.FromResult(executionResult is { Succeeded: true }))
                .ConfigureAwait(false);
        }

        private static bool IsProtectedWindowsService(string serviceName) =>
            serviceName.Equals("WinDefend", StringComparison.OrdinalIgnoreCase) ||
            serviceName.Equals("WdNisSvc", StringComparison.OrdinalIgnoreCase) ||
            serviceName.Equals("MpsSvc", StringComparison.OrdinalIgnoreCase) ||
            serviceName.Equals("SecurityHealthService", StringComparison.OrdinalIgnoreCase);
    }
}
