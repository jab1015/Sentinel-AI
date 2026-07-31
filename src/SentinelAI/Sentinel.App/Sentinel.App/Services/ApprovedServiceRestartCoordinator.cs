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
    /// Connects Sentinel's short-lived approval workflow to the concrete Windows
    /// service restart implementation. The exact action and target are rechecked
    /// before execution and success requires independent post-action verification.
    /// </summary>
    public sealed class ApprovedServiceRestartCoordinator
    {
        private readonly ApprovedRemediationExecutor _approvedExecutor;
        private readonly ServiceRemediationService _serviceRemediation;

        public ApprovedServiceRestartCoordinator(
            ApprovedRemediationExecutor? approvedExecutor = null,
            ServiceRemediationService? serviceRemediation = null)
        {
            _approvedExecutor = approvedExecutor ?? new ApprovedRemediationExecutor();
            _serviceRemediation = serviceRemediation ?? new ServiceRemediationService();
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

            if (!string.Equals(request.Action, "restart-service", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                    "This approval is not for a Windows service restart. Sentinel made no system change.");
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
                    {
                        return false;
                    }

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
