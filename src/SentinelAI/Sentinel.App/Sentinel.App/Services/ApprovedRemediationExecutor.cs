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
    /// Success is never assumed: independent follow-up evidence must confirm it.
    /// </summary>
    public sealed class ApprovedRemediationExecutor
    {
        private const int VerificationAttempts = 3;
        private static readonly TimeSpan VerificationDelay = TimeSpan.FromSeconds(1);

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
                        "The approved action was attempted. Sentinel is waiting for independent follow-up evidence before reporting success.",
                        attemptedAt);
                }

                for (int attempt = 1; attempt <= VerificationAttempts; attempt++)
                {
                    if (await verifyAsync().ConfigureAwait(false))
                    {
                        return ApprovedRemediationResult.Success(
                            request.Title,
                            "Sentinel completed the approved action and follow-up evidence verified the expected result.",
                            attemptedAt);
                    }

                    if (attempt < VerificationAttempts)
                    {
                        await Task.Delay(VerificationDelay).ConfigureAwait(false);
                    }
                }

                return ApprovedRemediationResult.VerificationFailed(
                    request.Title,
                    "The approved action was attempted, but repeated follow-up checks did not confirm the expected result. Sentinel will continue investigating and will not report this action as successful.",
                    attemptedAt);
            }
            catch (Exception ex)
            {
                return ApprovedRemediationResult.Failure(
                    "Approved action could not complete",
                    $"Sentinel could not complete or verify the approved action and will continue monitoring. {ex.Message}");
            }
        }

        public enum RemediationOutcome
        {
            NotAttempted,
            VerificationPending,
            VerificationFailed,
            VerifiedSuccess,
            ExecutionFailed
        }

        public sealed record ApprovedRemediationResult(
            bool Attempted,
            bool Verified,
            RemediationOutcome Outcome,
            string Title,
            string Summary,
            DateTimeOffset? AttemptedAt)
        {
            public bool RequiresContinuedInvestigation =>
                Outcome is RemediationOutcome.VerificationPending or
                    RemediationOutcome.VerificationFailed or
                    RemediationOutcome.ExecutionFailed;

            public static ApprovedRemediationResult NotAttempted(string summary) =>
                new(false, false, RemediationOutcome.NotAttempted, string.Empty, summary, null);

            public static ApprovedRemediationResult VerificationPending(string title, string summary, DateTimeOffset attemptedAt) =>
                new(true, false, RemediationOutcome.VerificationPending, title, summary, attemptedAt);

            public static ApprovedRemediationResult VerificationFailed(string title, string summary, DateTimeOffset attemptedAt) =>
                new(true, false, RemediationOutcome.VerificationFailed, title, summary, attemptedAt);

            public static ApprovedRemediationResult Success(string title, string summary, DateTimeOffset attemptedAt) =>
                new(true, true, RemediationOutcome.VerifiedSuccess, title, summary, attemptedAt);

            public static ApprovedRemediationResult Failure(string title, string summary) =>
                new(true, false, RemediationOutcome.ExecutionFailed, title, summary, DateTimeOffset.Now);
        }
    }
}
