/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Decides whether a completed investigation may proceed automatically,
    /// requires user approval, or must remain observation-only. This coordinator
    /// does not bypass RemediationPolicy and does not execute moderate/high-risk
    /// changes silently.
    /// </summary>
    public sealed class AutonomousProtectionCoordinator
    {
        private const int MinimumAutomaticConfidencePercent = 80;
        private readonly RemediationPolicy _policy = new();

        public AutonomousProtectionDecision Evaluate(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            if (!snapshot.RemediationAvailable ||
                string.Equals(snapshot.RemediationAction, "None", StringComparison.OrdinalIgnoreCase))
            {
                return AutonomousProtectionDecision.Observe(
                    "No autonomous remediation is required.");
            }

            RemediationPolicy.RemediationAction action = MapAction(snapshot.RemediationAction);
            RemediationPolicy.RemediationRisk risk = MapRisk(action);

            if (risk == RemediationPolicy.RemediationRisk.Low &&
                snapshot.GuidanceConfidencePercent < MinimumAutomaticConfidencePercent)
            {
                return AutonomousProtectionDecision.Observe(
                    $"Sentinel will continue monitoring because automatic protection requires at least {MinimumAutomaticConfidencePercent}% evidence confidence.");
            }

            RemediationPolicy.RemediationDecision policyDecision = _policy.Evaluate(
                new RemediationPolicy.RemediationRequest(
                    action,
                    risk,
                    HasVerifiedEvidence: HasVerifiedEvidence(snapshot),
                    IsWindowsProtectedComponent: IsProtectedWindowsTarget(snapshot),
                    RequiresElevation: RequiresElevation(action),
                    CanRequestElevation: true));

            if (!policyDecision.Allowed)
            {
                return AutonomousProtectionDecision.Observe(policyDecision.Explanation);
            }

            if (policyDecision.RequiresUserApproval || snapshot.RemediationRequiresUserApproval)
            {
                return new AutonomousProtectionDecision(
                    CanExecuteAutomatically: false,
                    RequiresUserApproval: true,
                    Action: snapshot.RemediationAction,
                    Target: snapshot.RemediationTarget,
                    Summary: policyDecision.Explanation);
            }

            return new AutonomousProtectionDecision(
                CanExecuteAutomatically: true,
                RequiresUserApproval: false,
                Action: snapshot.RemediationAction,
                Target: snapshot.RemediationTarget,
                Summary: policyDecision.Explanation);
        }

        private static bool HasVerifiedEvidence(SystemSnapshot snapshot) =>
            snapshot.InvestigationRequiresAttention &&
            !string.IsNullOrWhiteSpace(snapshot.InvestigationReasonCode) &&
            !string.Equals(snapshot.InvestigationReasonCode, "initializing", StringComparison.OrdinalIgnoreCase) &&
            snapshot.GuidanceConfidencePercent >= 70;

        private static bool IsProtectedWindowsTarget(SystemSnapshot snapshot) =>
            string.Equals(snapshot.RemediationTarget, "Microsoft Defender", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snapshot.RemediationTarget, "Windows Firewall", StringComparison.OrdinalIgnoreCase);

        private static bool RequiresElevation(RemediationPolicy.RemediationAction action) =>
            action is RemediationPolicy.RemediationAction.BlockNetworkEndpoint or
                RemediationPolicy.RemediationAction.QuarantineFile or
                RemediationPolicy.RemediationAction.RestoreQuarantinedFile or
                RemediationPolicy.RemediationAction.RestartService;

        private static RemediationPolicy.RemediationRisk MapRisk(RemediationPolicy.RemediationAction action) =>
            action switch
            {
                RemediationPolicy.RemediationAction.RetryTransientOperation => RemediationPolicy.RemediationRisk.Low,
                RemediationPolicy.RemediationAction.RefreshSecurityState => RemediationPolicy.RemediationRisk.Low,
                RemediationPolicy.RemediationAction.None => RemediationPolicy.RemediationRisk.High,
                _ => RemediationPolicy.RemediationRisk.Moderate
            };

        private static RemediationPolicy.RemediationAction MapAction(string action) =>
            action switch
            {
                "contain-process" => RemediationPolicy.RemediationAction.TerminateProcess,
                "block-outbound-endpoint" => RemediationPolicy.RemediationAction.BlockNetworkEndpoint,
                "quarantine-file" => RemediationPolicy.RemediationAction.QuarantineFile,
                "restore-quarantined-file" => RemediationPolicy.RemediationAction.RestoreQuarantinedFile,
                "restart-service" => RemediationPolicy.RemediationAction.RestartService,
                "retry-transient-operation" => RemediationPolicy.RemediationAction.RetryTransientOperation,
                "refresh-security-state" => RemediationPolicy.RemediationAction.RefreshSecurityState,
                _ => RemediationPolicy.RemediationAction.None
            };

        public sealed record AutonomousProtectionDecision(
            bool CanExecuteAutomatically,
            bool RequiresUserApproval,
            string Action,
            string Target,
            string Summary)
        {
            public static AutonomousProtectionDecision Observe(string summary) =>
                new(false, false, "None", "None", summary);
        }
    }
}
