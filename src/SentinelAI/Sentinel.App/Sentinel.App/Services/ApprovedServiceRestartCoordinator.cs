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
    /// Executes only short-lived, approval-gated Windows service restarts.
    /// Other remediation actions are routed to their dedicated coordinators by
    /// ApprovedRemediationDispatcher and fail closed here.
    /// </summary>
    public sealed class ApprovedServiceRestartCoordinator
    {
        private readonly ApprovedRemediationExecutor _approvedExecutor;
        private readonly ServiceRemediationService _serviceRemediation;
        private readonly StoreSubscriptionService _subscriptionService;

        public ApprovedServiceRestartCoordinator(
            ApprovedRemediationExecutor? approvedExecutor = null,
            ServiceRemediationService? serviceRemediation = null,
            StoreSubscriptionService? subscriptionService = null)
        {
            _approvedExecutor = approvedExecutor ?? new ApprovedRemediationExecutor();
            _serviceRemediation = serviceRemediation ?? new ServiceRemediationService();
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
                    "Free local monitoring remains active, but an active Sentinel AI subscription is required before a service repair can be made.");
            }

            if (!string.Equals(request.Action, "restart-service", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                    "This coordinator only accepts approved Windows service restarts. Sentinel made no system change.");
            }

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
                        return false;

                    return await _serviceRemediation.IsRunningAsync(serviceName, cancellationToken)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false);
        }

        private static bool IsProtectedWindowsService(string serviceName) =>
            serviceName.Equals("WinDefend", StringComparison.OrdinalIgnoreCase) ||
            serviceName.Equals("WdNisSvc", StringComparison.OrdinalIgnoreCase) ||
            serviceName.Equals("MpsSvc", StringComparison.OrdinalIgnoreCase) ||
            serviceName.Equals("SecurityHealthService", StringComparison.OrdinalIgnoreCase);
    }
}
