/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Central safety policy for any Sentinel action that changes system state.
    /// Investigation and guidance remain read-only unless this policy explicitly
    /// permits an action and any required user approval has been obtained.
    /// </summary>
    public sealed class RemediationPolicy
    {
        public RemediationDecision Evaluate(RemediationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Action == RemediationAction.None)
            {
                return Deny("No remediation action was requested.");
            }

            if (!request.HasVerifiedEvidence)
            {
                return Deny("Sentinel has not verified enough evidence to change the system safely.");
            }

            if (request.IsWindowsProtectedComponent)
            {
                return Deny("Sentinel will not automatically modify a protected Windows component.");
            }

            if (request.RiskLevel == RemediationRisk.High)
            {
                return Deny("High-risk remediation requires a dedicated, verified workflow before it can be offered.");
            }

            if (request.RequiresElevation && !request.CanRequestElevation)
            {
                return Deny("This action requires administrator permission that Sentinel cannot currently request safely.");
            }

            if (request.RiskLevel == RemediationRisk.Low)
            {
                return request.Action switch
                {
                    RemediationAction.RetryTransientOperation => AllowAutomatic(
                        "Retry the temporary operation",
                        "Sentinel may retry this verified transient operation automatically and will verify the result."),

                    RemediationAction.RefreshSecurityState => AllowAutomatic(
                        "Refresh security state",
                        "Sentinel may refresh verified security state automatically because this does not weaken protection or remove user data."),

                    _ => Deny("This action is not approved for automatic remediation. Sentinel will not change the system silently.")
                };
            }

            return request.Action switch
            {
                RemediationAction.TerminateProcess => RequireApproval(
                    request.Action,
                    "Close the selected process",
                    "The process will be stopped only after you approve the exact process Sentinel identified."),

                RemediationAction.BlockNetworkEndpoint => RequireApproval(
                    request.Action,
                    "Block the selected network activity",
                    "A Windows Firewall rule will be created only after you approve the exact target and rule."),

                RemediationAction.QuarantineFile => RequireApproval(
                    request.Action,
                    "Quarantine the selected file",
                    "The file will be isolated only after you approve the exact file and Sentinel records how to restore it."),

                RemediationAction.RestoreQuarantinedFile => RequireApproval(
                    request.Action,
                    "Restore the quarantined file",
                    "The file will be restored only after you approve the destination and Sentinel verifies the quarantine record."),

                RemediationAction.RestartService => RequireApproval(
                    request.Action,
                    "Restart the selected service",
                    "The service will be restarted only after Sentinel verifies its current state and you approve the action."),

                _ => Deny("This remediation action is not supported by the current safety policy.")
            };
        }

        private static RemediationDecision AllowAutomatic(string title, string explanation) =>
            new(
                Allowed: true,
                RequiresUserApproval: false,
                Title: title,
                Explanation: explanation,
                ApprovalScope: string.Empty,
                ApprovalExpiresAfter: null);

        private static RemediationDecision RequireApproval(
            RemediationAction action,
            string title,
            string explanation) =>
            new(
                Allowed: true,
                RequiresUserApproval: true,
                Title: title,
                Explanation: explanation,
                ApprovalScope: action.ToString(),
                ApprovalExpiresAfter: TimeSpan.FromMinutes(2));

        private static RemediationDecision Deny(string explanation) =>
            new(
                Allowed: false,
                RequiresUserApproval: false,
                Title: "Action unavailable",
                Explanation: explanation,
                ApprovalScope: string.Empty,
                ApprovalExpiresAfter: null);

        public sealed record RemediationRequest(
            RemediationAction Action,
            RemediationRisk RiskLevel,
            bool HasVerifiedEvidence,
            bool IsWindowsProtectedComponent,
            bool RequiresElevation,
            bool CanRequestElevation);

        public sealed record RemediationDecision(
            bool Allowed,
            bool RequiresUserApproval,
            string Title,
            string Explanation,
            string ApprovalScope,
            TimeSpan? ApprovalExpiresAfter);

        public enum RemediationAction
        {
            None,
            TerminateProcess,
            BlockNetworkEndpoint,
            QuarantineFile,
            RestoreQuarantinedFile,
            RestartService,
            RetryTransientOperation,
            RefreshSecurityState
        }

        public enum RemediationRisk
        {
            Low,
            Moderate,
            High
        }
    }
}
