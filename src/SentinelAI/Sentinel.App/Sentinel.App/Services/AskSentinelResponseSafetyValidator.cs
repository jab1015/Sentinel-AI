/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Structural fail-safe for deterministic Ask Sentinel responses.
    ///
    /// Security/action claims are not authorized by scanning English phrases. Cloud-model prose
    /// is kept out of the security-state display path by ExternalInvestigationGateway; action and
    /// protection outcomes shown to the user are rendered by deterministic application code from
    /// verified snapshot/history records. This validator checks structural invariants only rather
    /// than pretending that wording analysis can prove a claim is supported.
    /// </summary>
    public sealed class AskSentinelResponseSafetyValidator
    {
        private const string InsufficientEvidence =
            "Sentinel does not yet have enough verified information to answer that question.";

        public ValidationResult Validate(
            AskSentinelResponseOrchestrator.AskSentinelResponse response,
            SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(response);
            ArgumentNullException.ThrowIfNull(snapshot);

            if (string.IsNullOrWhiteSpace(response.Answer))
                return Block("Ask Sentinel produced an empty response.");

            if (response.EvidenceCount <= 0)
                return Block("No verified evidence was available to support the response.");

            if (response.EvidenceTimestamp == default)
                return Block("The response did not identify the verified evidence snapshot it was based on.");

            if (response.EvidenceTimestamp > DateTimeOffset.UtcNow.AddMinutes(5))
                return Block("The response evidence timestamp is invalid.");

            if (response.UsedInvestigationHistory && response.IsInsufficientEvidence)
                return Block("History was marked as used even though the response reports insufficient evidence.");

            if (string.IsNullOrWhiteSpace(response.GroundingSummary))
                return Block("The response did not retain grounding/provenance information.");

            return new ValidationResult(
                IsSafe: true,
                Answer: response.Answer,
                Reason: "Response passed structural safety validation; security/action wording is produced only by deterministic application paths.");
        }

        private static ValidationResult Block(string reason) =>
            new(
                IsSafe: false,
                Answer: InsufficientEvidence,
                Reason: reason);

        public sealed record ValidationResult(
            bool IsSafe,
            string Answer,
            string Reason);
    }
}
