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
    /// Connects Sentinel's short-lived remediation approval to the verified process
    /// containment service. The exact action/target must still match the current
    /// investigation immediately before execution.
    /// </summary>
    public sealed class ApprovedProcessContainmentCoordinator
    {
        private readonly ApprovedRemediationExecutor _approvedExecutor;
        private readonly ProcessContainmentService _processContainment;

        public ApprovedProcessContainmentCoordinator(
            ApprovedRemediationExecutor? approvedExecutor = null,
            ProcessContainmentService? processContainment = null)
        {
            _approvedExecutor = approvedExecutor ?? new ApprovedRemediationExecutor();
            _processContainment = processContainment ?? new ProcessContainmentService();
        }

        public async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation)
        {
            ArgumentNullException.ThrowIfNull(currentSnapshot);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(validation);

            if (!string.Equals(request.Action, "contain-process", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                    "This approval is not for process containment. Sentinel made no system change.");
            }

            ProcessContainmentService.ProcessContainmentResult? containmentResult = null;

            return await _approvedExecutor.ExecuteAsync(
                currentSnapshot,
                request,
                validation,
                executeAsync: async () =>
                {
                    containmentResult = await _processContainment
                        .ContainAsync(request.Target)
                        .ConfigureAwait(false);

                    if (!containmentResult.Succeeded)
                    {
                        throw new InvalidOperationException(containmentResult.Summary);
                    }
                },
                verifyAsync: () => Task.FromResult(containmentResult?.Succeeded == true),
                actionWasAttempted: () => containmentResult?.Attempted == true,
                noActionSummary: () => containmentResult?.Summary ?? "The approved process was no longer running. No change was needed.")
                .ConfigureAwait(false);
        }
    }
}
