/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    public enum AskSentinelProvenanceLabel
    {
        VerifiedFact,
        Observed,
        Inferred,
        ActionVerified,
        Advisory
    }

    /// <summary>
    /// Structural fail-safe for deterministic Ask Sentinel responses.
    ///
    /// The orchestrator validates its initial response, but UI composition can replace that
    /// response later. ValidateForDisplay is therefore the final trust boundary immediately
    /// before user-visible output. It does not treat English wording as evidence. Instead,
    /// provenance is supplied by the deterministic call path and wording checks are used only
    /// as a fail-closed defense that prevents advisory/inferred text from asserting completed
    /// Sentinel/Defender/firewall actions.
    /// </summary>
    public sealed class AskSentinelResponseSafetyValidator
    {
        private const string InsufficientEvidence =
            "Sentinel does not yet have enough verified information to answer that question.";

        private static readonly string[] VerifiedActionOutcomePhrases =
        {
            "sentinel blocked",
            "sentinel has blocked",
            "sentinel stopped",
            "sentinel has stopped",
            "sentinel contained",
            "sentinel has contained",
            "sentinel quarantined",
            "sentinel has quarantined",
            "sentinel removed",
            "sentinel has removed",
            "sentinel deleted",
            "sentinel has deleted",
            "sentinel repaired",
            "sentinel has repaired",
            "sentinel fixed",
            "sentinel has fixed",
            "repair completed",
            "repair was completed",
            "repair completed successfully",
            "defender blocked",
            "defender has blocked",
            "microsoft defender blocked",
            "microsoft defender has blocked",
            "defender removed",
            "defender has removed",
            "microsoft defender removed",
            "microsoft defender has removed",
            "firewall rule was applied",
            "firewall rule has been applied",
            "firewall applied",
            "firewall has blocked",
            "windows firewall blocked",
            "windows firewall has blocked",
            "threat was blocked",
            "threat has been blocked",
            "threat was contained",
            "threat has been contained",
            "threat was quarantined",
            "threat has been quarantined",
            "threat was removed",
            "threat has been removed",
            "threat was eliminated",
            "threat has been eliminated",
            "malware was blocked",
            "malware has been blocked",
            "malware was removed",
            "malware has been removed",
            "file was quarantined",
            "file has been quarantined",
            "file was removed",
            "file has been removed"
        };

        public ValidationResult Validate(
            AskSentinelResponseOrchestrator.AskSentinelResponse response,
            SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(response);
            ArgumentNullException.ThrowIfNull(snapshot);

            if (string.IsNullOrWhiteSpace(response.Answer))
                return Block("Ask Sentinel produced an empty response.", AskSentinelProvenanceLabel.Advisory);

            if (response.EvidenceCount <= 0)
                return Block("No verified evidence was available to support the response.", AskSentinelProvenanceLabel.Advisory);

            if (response.EvidenceTimestamp == default)
                return Block("The response did not identify the verified evidence snapshot it was based on.", AskSentinelProvenanceLabel.Advisory);

            if (response.EvidenceTimestamp > DateTimeOffset.UtcNow.AddMinutes(5))
                return Block("The response evidence timestamp is invalid.", AskSentinelProvenanceLabel.Advisory);

            if (response.UsedInvestigationHistory && response.IsInsufficientEvidence)
                return Block("History was marked as used even though the response reports insufficient evidence.", AskSentinelProvenanceLabel.Advisory);

            if (string.IsNullOrWhiteSpace(response.GroundingSummary))
                return Block("The response did not retain grounding/provenance information.", AskSentinelProvenanceLabel.Advisory);

            return new ValidationResult(
                IsSafe: true,
                Answer: response.Answer,
                Reason: "Response passed structural safety validation.",
                Provenance: AskSentinelProvenanceLabel.Observed);
        }

        public ValidationResult ValidateForDisplay(
            AskSentinelResponseOrchestrator.AskSentinelResponse response,
            SystemSnapshot snapshot,
            AskSentinelProvenanceLabel provenance)
        {
            ValidationResult structural = Validate(response, snapshot);
            if (!structural.IsSafe)
                return structural with { Provenance = provenance };

            if (provenance is AskSentinelProvenanceLabel.Advisory or AskSentinelProvenanceLabel.Inferred)
            {
                if (ContainsVerifiedActionOutcomeClaim(response.Answer))
                {
                    return Block(
                        "Advisory or inferred text attempted to assert a completed security/remediation action without deterministic action verification.",
                        provenance);
                }
            }

            // Only the answer that crosses the final display-safety boundary is eligible
            // for short-lived conversational memory. The store treats it as context, never
            // as independent proof of current machine state or a completed action.
            AskSentinelConversationContextStore.RememberValidatedAnswer(response.Answer);

            return new ValidationResult(
                IsSafe: true,
                Answer: response.Answer,
                Reason: $"Response passed final display validation as {provenance}.",
                Provenance: provenance);
        }

        internal static bool ContainsVerifiedActionOutcomeClaim(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer)) return false;
            foreach (string phrase in VerifiedActionOutcomePhrases)
            {
                if (answer.Contains(phrase, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static ValidationResult Block(string reason, AskSentinelProvenanceLabel provenance) =>
            new(
                IsSafe: false,
                Answer: InsufficientEvidence,
                Reason: reason,
                Provenance: provenance);

        public sealed record ValidationResult(
            bool IsSafe,
            string Answer,
            string Reason,
            AskSentinelProvenanceLabel Provenance);
    }
}
