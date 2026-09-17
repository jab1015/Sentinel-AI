/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts verified Ask Sentinel evidence into a terminal resolution state.
    /// This planner never invents a repair. It only reports repair paths that are
    /// already represented by Sentinel's verified remediation state.
    /// </summary>
    public sealed class AskSentinelResolutionPlanner
    {
        public AskSentinelResolutionPlan CreatePlan(
            string question,
            SystemSnapshot snapshot,
            string verifiedFinding,
            bool deeperDiagnosticAlreadyCompleted = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            ArgumentNullException.ThrowIfNull(snapshot);

            string q = question.Trim().ToLowerInvariant();
            string finding = verifiedFinding?.Trim() ?? string.Empty;

            if (snapshot.RemediationAttempted)
            {
                string outcome = snapshot.RemediationSucceeded
                    ? Safe(snapshot.RemediationOutcomeSummary, "Sentinel verified that the remediation completed successfully.")
                    : Safe(snapshot.RemediationOutcomeSummary, "Sentinel attempted remediation but could not verify success.");
                return new AskSentinelResolutionPlan(
                    snapshot.RemediationSucceeded ? AskSentinelResolutionDisposition.Resolved : AskSentinelResolutionDisposition.CannotRepairSafely,
                    "Remediation result",
                    outcome,
                    false,
                    string.Empty,
                    string.Empty);
            }

            if (snapshot.AutonomousProtectionAttempted)
            {
                string outcome = snapshot.AutonomousProtectionSucceeded
                    ? Safe(snapshot.AutonomousProtectionOutcomeSummary, "Sentinel verified that the protection action completed successfully.")
                    : Safe(snapshot.AutonomousProtectionOutcomeSummary, "Sentinel attempted the protection action but could not verify success.");
                return new AskSentinelResolutionPlan(
                    snapshot.AutonomousProtectionSucceeded ? AskSentinelResolutionDisposition.Resolved : AskSentinelResolutionDisposition.CannotRepairSafely,
                    "Protection result",
                    outcome,
                    false,
                    string.Empty,
                    string.Empty);
            }

            if (snapshot.AutonomousProtectionRequiresUserApproval &&
                HasValue(snapshot.AutonomousProtectionAction) &&
                HasValue(snapshot.AutonomousProtectionTarget))
            {
                return new AskSentinelResolutionPlan(
                    AskSentinelResolutionDisposition.ApprovalRequired,
                    "A verified Sentinel action is available",
                    $"Sentinel has a verified action ready: {DescribeAction(snapshot.AutonomousProtectionAction)} '{snapshot.AutonomousProtectionTarget}'. " +
                    "This is an exact-target action backed by Sentinel's approval and post-action verification workflow. Sentinel should not perform it silently; the user must approve it first.",
                    true,
                    snapshot.AutonomousProtectionAction,
                    snapshot.AutonomousProtectionTarget);
            }

            // Driver remediation must use the dedicated exact-device repair workflow even
            // when the generic snapshot also advertises a review-driver-repair action.
            // That workflow is the one that prepares a signed package and surfaces the
            // actual approval controls in Ask Sentinel.
            if (IsDriverQuestion(q, finding) || IsDriverRemediation(snapshot.RemediationAction))
            {
                return new AskSentinelResolutionPlan(
                    AskSentinelResolutionDisposition.RepairCheckAvailable,
                    "Sentinel can check for a safe driver repair",
                    "Sentinel has a dedicated driver-repair workflow that requires an exact device identity and an exact Microsoft-signed update match before installation is offered. If no exact verified package is available, Sentinel should conclude that it cannot safely repair the driver automatically.",
                    true,
                    "driver-repair-check",
                    HasValue(snapshot.RemediationTarget) ? snapshot.RemediationTarget : string.Empty);
            }

            if (snapshot.RemediationAvailable && HasValue(snapshot.RemediationAction))
            {
                string target = HasValue(snapshot.RemediationTarget) ? snapshot.RemediationTarget : "the verified finding";
                return new AskSentinelResolutionPlan(
                    snapshot.RemediationRequiresUserApproval
                        ? AskSentinelResolutionDisposition.ManualReviewRequired
                        : AskSentinelResolutionDisposition.RepairAvailable,
                    snapshot.RemediationRequiresUserApproval
                        ? "A repair path exists but no Ask Sentinel executor is available for it yet"
                        : "A verified repair path is available",
                    Safe(snapshot.RemediationSummary,
                        $"Sentinel has a verified remediation path for {target}: {snapshot.RemediationAction}."),
                    snapshot.RemediationRequiresUserApproval,
                    snapshot.RemediationAction,
                    target);
            }

            if (IsStartupQuestion(q))
            {
                if (!deeperDiagnosticAlreadyCompleted)
                {
                    return new AskSentinelResolutionPlan(
                        AskSentinelResolutionDisposition.MoreDiagnosticsAvailable,
                        "Sentinel can investigate startup more deeply",
                        "Run Sentinel's deeper local startup/sign-in check before changing startup apps, services, tasks, or system settings.",
                        false,
                        "deep-startup-diagnostic",
                        string.Empty);
                }

                if (snapshot.FlaggedServiceCount > 0 && HasValue(snapshot.PrimaryFlaggedServiceName))
                {
                    return new AskSentinelResolutionPlan(
                        AskSentinelResolutionDisposition.ManualReviewRequired,
                        "A current service finding needs review",
                        $"Sentinel currently flags service '{snapshot.PrimaryFlaggedServiceName}': {snapshot.PrimaryFlaggedServiceReason}. " +
                        "A service restart or configuration change should be offered only if Sentinel's remediation engine produces an exact approved action for that service. No such approved action is present in the current snapshot.",
                        false,
                        string.Empty,
                        snapshot.PrimaryFlaggedServiceName);
                }

                return new AskSentinelResolutionPlan(
                    AskSentinelResolutionDisposition.CannotRepairSafely,
                    "Sentinel cannot safely auto-fix this startup delay from the current evidence",
                    "The deeper local investigation did not produce an exact current remediation target that Sentinel can verify and repair safely. Historical service failures, resource snapshots, or heuristic task/startup flags are useful leads, but they are not sufficient justification to restart, disable, delete, or reconfigure Windows components automatically. Sentinel should stop the repair loop here, keep monitoring, and reopen remediation only if a current exact cause or approved action is verified.",
                    false,
                    string.Empty,
                    string.Empty);
            }

            if (!snapshot.InvestigationRequiresAttention)
            {
                return new AskSentinelResolutionPlan(
                    AskSentinelResolutionDisposition.NoActionNeeded,
                    "No repair is currently justified",
                    "Sentinel does not currently have a verified finding that requires remediation. It should continue monitoring rather than change the system without evidence.",
                    false,
                    string.Empty,
                    string.Empty);
            }

            if (snapshot.InvestigationShouldEscalate || snapshot.InvestigationIsRecurring)
            {
                return new AskSentinelResolutionPlan(
                    AskSentinelResolutionDisposition.MoreDiagnosticsAvailable,
                    "More verified evidence is needed before repair",
                    "Sentinel has an unresolved or recurring verified finding, but the current snapshot does not expose an approved remediation action. It should continue targeted diagnostics and only offer a repair if an exact supported action and target are verified.",
                    false,
                    string.Empty,
                    string.Empty);
            }

            return new AskSentinelResolutionPlan(
                AskSentinelResolutionDisposition.CannotRepairSafely,
                "Sentinel cannot safely repair this automatically",
                "Sentinel has a verified finding, but there is no exact remediation action in the current evidence that maps to a supported, independently verifiable executor. Sentinel should not keep cycling through generic next steps or invent a fix.",
                false,
                string.Empty,
                string.Empty);
        }

        private static bool IsStartupQuestion(string q) =>
            q.Contains("login") || q.Contains("log in") || q.Contains("logon") || q.Contains("log on") ||
            q.Contains("sign in") || q.Contains("signin") || q.Contains("sign-in") || q.Contains("boot") ||
            q.Contains("startup") || q.Contains("start up");

        private static bool IsDriverQuestion(string q, string finding) =>
            q.Contains("driver") || finding.Contains("driver", StringComparison.OrdinalIgnoreCase) ||
            finding.Contains("device manager", StringComparison.OrdinalIgnoreCase);

        private static bool IsDriverRemediation(string? action) =>
            !string.IsNullOrWhiteSpace(action) &&
            (action.Contains("driver", StringComparison.OrdinalIgnoreCase) ||
             action.Equals("review-driver-repair", StringComparison.OrdinalIgnoreCase));

        private static string DescribeAction(string action) => action switch
        {
            "restart-service" => "restart service",
            "block-outbound-endpoint" => "block outbound endpoint",
            "contain-process" => "contain process",
            "quarantine-file" => "quarantine file",
            "restore-quarantined-file" => "restore quarantined file",
            _ => action.Replace('-', ' ')
        };

        private static bool HasValue(string? value) =>
            !string.IsNullOrWhiteSpace(value) && !value.Equals("None", StringComparison.OrdinalIgnoreCase);

        private static string Safe(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    public enum AskSentinelResolutionDisposition
    {
        Resolved,
        NoActionNeeded,
        MoreDiagnosticsAvailable,
        RepairCheckAvailable,
        RepairAvailable,
        ApprovalRequired,
        ManualReviewRequired,
        CannotRepairSafely
    }

    public sealed record AskSentinelResolutionPlan(
        AskSentinelResolutionDisposition Disposition,
        string Title,
        string Summary,
        bool RequiresUserApproval,
        string Action,
        string Target);
}
