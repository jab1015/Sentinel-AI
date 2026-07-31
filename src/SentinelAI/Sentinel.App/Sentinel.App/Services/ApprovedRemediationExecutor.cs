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
    /// Success is never assumed: a separate verification callback must confirm
    /// that the intended system-state change actually occurred.
    /// </summary>
    public sealed class ApprovedRemediationExecutor
    {
        public async Task<ApprovedRemediationResult> ExecuteAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            Func<Task> executeAsync,
            Func<Task<bool>>? verifyAsync = null)
        {
            ArgumentNullException.ThrowIfNull(currentSnapshot);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(validation);
            ArgumentNullException.ThrowIfNull(executeAsync);

            if (!validation.IsApproved)
            {
                return ApprovedRemediationResult.NotAttempted(validation.Message);
            }

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
                DateTimeOffset attemptedAt = DateTimeOffset.Now;

                if (verifyAsync is null)
                {
                    return ApprovedRemediationResult.VerificationPending(
                        request.Title,
                        "The approved action was attempted. Sentinel has not yet independently verified the result.",
                        attemptedAt);
                }

                bool verified = await verifyAsync().ConfigureAwait(false);
                if (!verified)
                {
                    return ApprovedRemediationResult.VerificationFailed(
                        request.Title,
                        "The approved action was attempted, but follow-up evidence did not confirm the expected result. Sentinel will continue investigating and will not report this action as successful.",
                        attemptedAt);
                }

                return ApprovedRemediationResult.Success(
                    request.Title,
                    "Sentinel completed the approved action and follow-up evidence verified the expected result.",
                    attemptedAt);
            }
            catch (Exception ex)
            {
                return ApprovedRemediationResult.Failure(
                    "Approved action could not complete",
                    $"Sentinel could not complete or verify the approved action and will continue monitoring. {ex.Message}");
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

            public static ApprovedRemediationResult VerificationPending(string title, string summary, DateTimeOffset attemptedAt) =>
                new(true, false, title, summary, attemptedAt);

            public static ApprovedRemediationResult VerificationFailed(string title, string summary, DateTimeOffset attemptedAt) =>
                new(true, false, title, summary, attemptedAt);

            public static ApprovedRemediationResult Success(string title, string summary, DateTimeOffset attemptedAt) =>
                new(true, true, title, summary, attemptedAt);

            public static ApprovedRemediationResult Failure(string title, string summary) =>
                new(true, false, title, summary, DateTimeOffset.Now);
        }
    }
}
