/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Creates short-lived, exact-target approval requests for remediation that
    /// must never execute silently. Approval is bound to the investigation state
    /// that produced it so stale user consent cannot authorize a later action.
    /// </summary>
    public sealed class RemediationApprovalCoordinator
    {
        private static readonly TimeSpan ApprovalLifetime = TimeSpan.FromMinutes(2);

        public RemediationApprovalRequest? CreateRequest(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            if (!snapshot.AutonomousProtectionRequiresUserApproval ||
                string.IsNullOrWhiteSpace(snapshot.AutonomousProtectionAction) ||
                string.Equals(snapshot.AutonomousProtectionAction, "None", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(snapshot.AutonomousProtectionTarget) ||
                string.Equals(snapshot.AutonomousProtectionTarget, "None", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            DateTimeOffset createdAt = DateTimeOffset.Now;

            return new RemediationApprovalRequest(
                RequestId: Guid.NewGuid(),
                Action: snapshot.AutonomousProtectionAction,
                Target: snapshot.AutonomousProtectionTarget,
                ReasonCode: snapshot.InvestigationReasonCode,
                EvidenceConfidencePercent: snapshot.GuidanceConfidencePercent,
                CreatedAt: createdAt,
                ExpiresAt: createdAt.Add(ApprovalLifetime),
                Title: BuildTitle(snapshot.AutonomousProtectionAction),
                Summary: BuildSummary(snapshot));
        }

        public ApprovalValidationResult Validate(
            RemediationApprovalRequest request,
            SystemSnapshot currentSnapshot,
            bool userApproved)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(currentSnapshot);

            if (!userApproved)
            {
                return ApprovalValidationResult.Denied("You did not approve this action. Sentinel made no system change.");
            }

            if (DateTimeOffset.Now > request.ExpiresAt)
            {
                return ApprovalValidationResult.Denied("This approval expired because the system may have changed. Sentinel will investigate again before offering the action.");
            }

            bool stillMatches =
                currentSnapshot.InvestigationRequiresAttention &&
                currentSnapshot.AutonomousProtectionRequiresUserApproval &&
                string.Equals(request.Action, currentSnapshot.AutonomousProtectionAction, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(request.Target, currentSnapshot.AutonomousProtectionTarget, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(request.ReasonCode, currentSnapshot.InvestigationReasonCode, StringComparison.OrdinalIgnoreCase);

            if (!stillMatches)
            {
                return ApprovalValidationResult.Denied("The investigation changed before Sentinel could act, so the previous approval was discarded.");
            }

            if (currentSnapshot.GuidanceConfidencePercent < request.EvidenceConfidencePercent)
            {
                return ApprovalValidationResult.Denied("Evidence confidence decreased after approval. Sentinel will not proceed without re-investigating.");
            }

            return ApprovalValidationResult.Approved(
                "Approval verified. Sentinel may proceed with the exact action and target shown to you.");
        }

        private static string BuildTitle(string action) =>
            action switch
            {
                "contain-process" => "Close suspicious process?",
                "block-outbound-endpoint" => "Block suspicious network activity?",
                "quarantine-file" => "Quarantine suspicious file?",
                "restore-quarantined-file" => "Restore quarantined file?",
                "restart-service" => "Restart affected service?",
                _ => "Approve Sentinel action?"
            };

        private static string BuildSummary(SystemSnapshot snapshot) =>
            $"Sentinel investigated the condition and recommends '{snapshot.AutonomousProtectionAction}' for '{snapshot.AutonomousProtectionTarget}'. " +
            "The action will apply only to this exact target, and Sentinel will verify the result afterward.";

        public sealed record RemediationApprovalRequest(
            Guid RequestId,
            string Action,
            string Target,
            string ReasonCode,
            int EvidenceConfidencePercent,
            DateTimeOffset CreatedAt,
            DateTimeOffset ExpiresAt,
            string Title,
            string Summary);

        public sealed record ApprovalValidationResult(
            bool IsApproved,
            string Message)
        {
            public static ApprovalValidationResult Approved(string message) => new(true, message);
            public static ApprovalValidationResult Denied(string message) => new(false, message);
        }
    }
}
