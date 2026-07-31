/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Executes a user-approved remediation only after validating and consuming
    /// the exact short-lived approval issued for that action.
    /// </summary>
    public sealed class ApprovedRemediationExecutor
    {
        public async Task<ApprovedRemediationResult> ExecuteAsync(
            RemediationPolicy.RemediationDecision decision,
            RemediationApproval? approval,
            Func<Task> executeAsync)
        {
            ArgumentNullException.ThrowIfNull(decision);
            ArgumentNullException.ThrowIfNull(executeAsync);

            if (!decision.Allowed || !decision.RequiresUserApproval)
            {
                return ApprovedRemediationResult.NotAttempted(
                    "This action is not eligible for the user-approval execution path.");
            }

            if (string.IsNullOrWhiteSpace(decision.ApprovalScope) ||
                decision.ApprovalExpiresAfter is null)
            {
                return ApprovedRemediationResult.NotAttempted(
                    "Sentinel could not verify a safe approval scope for this action.");
            }

            if (approval is null || !approval.TryConsume(decision.ApprovalScope))
            {
                return ApprovedRemediationResult.NotAttempted(
                    "The approval is missing, expired, already used, or does not match this action. Sentinel made no change.");
            }

            try
            {
                await executeAsync().ConfigureAwait(false);

                return ApprovedRemediationResult.VerificationPending(
                    decision.Title,
                    "The approved action was attempted. Sentinel will report success only after follow-up evidence verifies the expected result.");
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
