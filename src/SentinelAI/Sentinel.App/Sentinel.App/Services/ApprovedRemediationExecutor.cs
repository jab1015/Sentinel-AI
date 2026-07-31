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
    /// Executes a remediation only after the approval coordinator has validated
    /// the user's short-lived consent against the current investigation state.
    /// </summary>
    public sealed class ApprovedRemediationExecutor
    {
        public async Task<ApprovedRemediationResult> ExecuteAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            Func<Task> executeAsync)
        {
            ArgumentNullException.ThrowIfNull(currentSnapshot);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(validation);
            ArgumentNullException.ThrowIfNull(executeAsync);

            if (!validation.IsApproved)
            {
                return ApprovedRemediationResult.NotAttempted(validation.Message);
            }

            // Defense in depth: never trust approval validation alone. Recheck the
            // exact action, target, reason, confidence, and expiration immediately
            // before changing system state.
            if (DateTimeOffset.Now > request.ExpiresAt ||
                !currentSnapshot.InvestigationRequiresAttention ||
                !currentSnapshot.AutonomousProtectionRequiresUserApproval ||
                !string.Equals(request.Action, currentSnapshot.AutonomousProtectionAction, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(request.Target, currentSnapshot.AutonomousProtectionTarget, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(request.ReasonCode, currentSnapshot.InvestigationReasonCode, StringComparison.OrdinalIgnoreCase) ||
                currentSnapshot.GuidanceConfidencePercent < request.EvidenceConfidencePercent)
            {
                return ApprovedRemediationResult.NotAttempted(
                    "The investigation changed after approval. Sentinel made no system change and will investigate again.");
            }

            try
            {
                await executeAsync().ConfigureAwait(false);

                return ApprovedRemediationResult.VerificationPending(
                    request.Title,
                    "The approved action was attempted for the exact target you approved. Sentinel will report success only after follow-up evidence verifies the expected result.");
            }
            catch (Exception ex)
            {
                return ApprovedRemediationResult.Failure(
                    "Approved action could not complete",
                    $"Sentinel could not complete the approved action and will continue monitoring. {ex.Message}");
            }
        }

        public sealed record ApprovedRemediationResult(
            bool Attempted,
            bool Verified,
            string Title,
            string Summary,
            DateTimeOffset? AttemptedAt)
        {
            public static ApprovedRemediationResult NotAttempted(string summary) =>
                new(false, false, string.Empty, summary, null);

            public static ApprovedRemediationResult VerificationPending(string title, string summary) =>
                new(true, false, title, summary, DateTimeOffset.Now);

            public static ApprovedRemediationResult Failure(string title, string summary) =>
                new(true, false, title, summary, DateTimeOffset.Now);
        }
    }
}
