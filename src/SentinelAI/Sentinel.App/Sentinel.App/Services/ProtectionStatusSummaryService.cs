/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts live deterministic protection/remediation state into user-facing
    /// status text. This is intentionally not AI-generated.
    /// </summary>
    public sealed class ProtectionStatusSummaryService
    {
        public ProtectionStatusSummary Create(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            string flagged = BuildFlaggedConditions(snapshot);

            if (IsAction(snapshot, "block-outbound-endpoint"))
            {
                return new(
                    "Network containment is ready for review",
                    flagged,
                    $"Sentinel has finished correlating the current network evidence and prepared containment for {snapshot.AutonomousProtectionTarget}. No firewall change has been made yet.",
                    "Network containment requires corroborating evidence, at least 80% confidence, an exact remote target, and your approval. Sentinel revalidates the same finding immediately before changing Windows Firewall.",
                    "Waiting for your approval. If approved, Sentinel blocks all outbound traffic to the remote IP represented by the flagged connection, verifies the rule, checks connectivity, and rolls the rule back if immediate general connectivity is lost.");
            }

            if (snapshot.FlaggedConnectionCount > 0)
            {
                string endpoint = Value(snapshot.PrimaryFlaggedConnectionRemoteEndpoint, "the flagged remote destination");
                string process = Value(snapshot.PrimaryFlaggedConnectionProcessName, "an unattributed process");
                string currentResponse;

                if (!snapshot.ConnectionIntelligenceHasCorroboratingEvidence)
                {
                    currentResponse =
                        $"Sentinel is monitoring and correlating {process} → {endpoint}. Current network confidence is {snapshot.ConnectionIntelligenceConfidenceScore}% and independent corroborating evidence has not been established, so Sentinel is not changing the firewall.";
                }
                else if (snapshot.ConnectionIntelligenceConfidenceScore < 80)
                {
                    currentResponse =
                        $"Sentinel has corroborating evidence for {process} → {endpoint}, but current confidence is {snapshot.ConnectionIntelligenceConfidenceScore}%. Sentinel is continuing the investigation instead of changing the firewall prematurely.";
                }
                else
                {
                    currentResponse =
                        $"Sentinel is continuing to re-evaluate {process} → {endpoint}. The evidence is corroborated at {snapshot.ConnectionIntelligenceConfidenceScore}% confidence, but the current investigation has not produced a valid exact-target containment approval state.";
                }

                return new(
                    "Flagged network condition under investigation",
                    flagged,
                    currentResponse,
                    "For network containment, Sentinel requires corroborating evidence, at least 80% confidence, an exact remote target, and a supported containment recommendation. The firewall change then requires your approval and a fresh pre-action revalidation.",
                    "No network containment is authorized yet. Sentinel continues monitoring and will expose Review & Approve only if the verified evidence crosses the containment threshold.");
            }

            if (IsAction(snapshot, "contain-process"))
            {
                return new(
                    "Process containment is ready for review",
                    flagged,
                    $"Sentinel has correlated enough evidence to prepare containment for {snapshot.AutonomousProtectionTarget}. The process has not been terminated.",
                    "Process containment is offered only for a verified exact process finding backed by supported correlated evidence. It requires your approval and process identity is revalidated immediately before execution.",
                    "Waiting for your approval. Sentinel will report success only if the exact approved process instance is contained and the result is independently verified.");
            }

            if (snapshot.FlaggedProcessCount > 0)
            {
                return new(
                    "Flagged process under investigation",
                    flagged,
                    $"Sentinel is correlating process, command-line, lineage, persistence, and network evidence for {Value(snapshot.PrimaryFlaggedProcessName, "the flagged process")}. A flagged process by itself is not treated as malware.",
                    "Sentinel only offers process containment when correlated evidence produces a supported exact process finding. Containment requires your approval and a fresh identity check before execution.",
                    "No process-changing action is authorized yet. Monitoring and correlation continue.");
            }

            if (snapshot.FlaggedServiceCount > 0)
            {
                return new(
                    "Flagged Windows service under review",
                    flagged,
                    $"Sentinel is reviewing the current service evidence for {Value(snapshot.PrimaryFlaggedServiceName, "the flagged service")} and comparing it with Windows event/state evidence.",
                    "A service change is not made from a flag alone. Sentinel needs an exact supported remediation path; restart actions require verification and your approval.",
                    IsAction(snapshot, "restart-service")
                        ? "A verified service restart is ready for your approval."
                        : "No service-changing action is authorized yet. Sentinel continues monitoring.");
            }

            if (snapshot.FlaggedStartupEntryCount > 0 || snapshot.FlaggedScheduledTaskCount > 0 || snapshot.AuthenticationAnomalyDetected)
            {
                return new(
                    "Persistence or authentication condition under investigation",
                    flagged,
                    "Sentinel is correlating the flagged persistence/authentication evidence with process, command-line, service, and network evidence. It does not disable startup entries, delete tasks, or change accounts from a heuristic flag alone.",
                    "A system-changing action is offered only when Sentinel can identify an exact supported target and verified remediation path. Unsupported or ambiguous findings remain observation-only.",
                    "No destructive or configuration-changing action is authorized from the current evidence.");
            }

            if (snapshot.InvestigationRequiresAttention)
            {
                string actionState = snapshot.AutonomousProtectionRequiresUserApproval &&
                                     !IsNone(snapshot.AutonomousProtectionAction)
                    ? $"A verified action is available and requires your approval: {snapshot.AutonomousProtectionAction} → {snapshot.AutonomousProtectionTarget}."
                    : snapshot.AutonomousProtectionCanExecute && !IsNone(snapshot.AutonomousProtectionAction)
                        ? $"Sentinel may perform the verified low-risk action automatically: {snapshot.AutonomousProtectionAction}."
                        : "The current finding remains investigation/guidance only; no supported system-changing action is authorized.";

                return new(
                    "Sentinel is investigating a verified condition",
                    flagged,
                    Value(snapshot.InvestigationSummary, snapshot.GuidanceWhatHappened),
                    "Sentinel separates detection from remediation. A finding becomes actionable only when the evidence maps to a supported exact-target action that passes remediation policy. Moderate security/system changes require approval; only narrow low-risk refresh/retry operations may run automatically.",
                    actionState);
            }

            return new(
                "No current flagged condition requires action",
                flagged,
                "Sentinel is continuing background monitoring and evidence collection. It has not found a current condition that justifies a security or system change.",
                "Sentinel does not change Windows merely because something looks unusual. It requires verified evidence and a supported exact-target remediation path. Moderate changes require your approval.",
                "No action is required right now.");
        }

        private static string BuildFlaggedConditions(SystemSnapshot snapshot)
        {
            List<string> parts = new();

            if (snapshot.FlaggedConnectionCount > 0)
                parts.Add($"Network: {snapshot.FlaggedConnectionCount} flagged ({Value(snapshot.PrimaryFlaggedConnectionRemoteEndpoint, "target unavailable")})");
            if (snapshot.FlaggedProcessCount > 0)
                parts.Add($"Processes: {snapshot.FlaggedProcessCount} flagged ({Value(snapshot.PrimaryFlaggedProcessName, "name unavailable")})");
            if (snapshot.FlaggedServiceCount > 0)
                parts.Add($"Services: {snapshot.FlaggedServiceCount} flagged ({Value(snapshot.PrimaryFlaggedServiceName, "name unavailable")})");
            if (snapshot.FlaggedStartupEntryCount > 0)
                parts.Add($"Startup: {snapshot.FlaggedStartupEntryCount} flagged ({Value(snapshot.PrimaryFlaggedStartupEntryName, "entry unavailable")})");
            if (snapshot.FlaggedScheduledTaskCount > 0)
                parts.Add($"Scheduled tasks: {snapshot.FlaggedScheduledTaskCount} flagged ({Value(snapshot.PrimaryFlaggedScheduledTaskName, "task unavailable")})");
            if (snapshot.AuthenticationAnomalyDetected)
                parts.Add($"Authentication: anomaly detected ({snapshot.AuthenticationAnomalyConfidenceScore}% confidence)");

            return parts.Count == 0
                ? "No currently flagged process, network, service, startup, scheduled-task, or authentication condition."
                : string.Join(" • ", parts);
        }

        private static bool IsAction(SystemSnapshot snapshot, string action) =>
            snapshot.AutonomousProtectionRequiresUserApproval &&
            string.Equals(snapshot.AutonomousProtectionAction, action, StringComparison.OrdinalIgnoreCase) &&
            !IsNone(snapshot.AutonomousProtectionTarget);

        private static bool IsNone(string? value) =>
            string.IsNullOrWhiteSpace(value) ||
            value.Equals("None", StringComparison.OrdinalIgnoreCase);

        private static string Value(string? value, string fallback) =>
            IsNone(value) ? fallback : value!.Trim();

        public sealed record ProtectionStatusSummary(
            string Headline,
            string FlaggedConditions,
            string CurrentResponse,
            string ActionCriteria,
            string ActionState);
    }
}
