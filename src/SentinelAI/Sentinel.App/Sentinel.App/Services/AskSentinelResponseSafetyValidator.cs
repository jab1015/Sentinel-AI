/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Final fail-safe validation for Ask Sentinel responses. This guard runs after
    /// response construction and blocks internally inconsistent claims before they
    /// reach the user.
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
            {
                return Block("Ask Sentinel produced an empty response.");
            }

            if (response.EvidenceCount <= 0)
            {
                return Block("No verified evidence was available to support the response.");
            }

            if (response.UsedInvestigationHistory && response.IsInsufficientEvidence)
            {
                return Block("History was marked as used even though the response reports insufficient evidence.");
            }

            if (ClaimsSuccessfulAction(response.Answer) &&
                !snapshot.RemediationSucceeded &&
                !snapshot.AutonomousProtectionSucceeded)
            {
                return Block("The response would claim a successful action without a verified success outcome.");
            }

            if (ClaimsActionWasPerformed(response.Answer) &&
                !snapshot.RemediationAttempted &&
                !snapshot.AutonomousProtectionAttempted)
            {
                return Block("The response would claim that Sentinel performed an action without a verified action attempt.");
            }

            if (ClaimsThreatFound(response.Answer) &&
                snapshot.FlaggedProcessCount <= 0 &&
                snapshot.FlaggedConnectionCount <= 0 &&
                snapshot.FlaggedServiceCount <= 0 &&
                snapshot.DefenderEnabled &&
                snapshot.FirewallEnabled)
            {
                return Block("The response would make an unsupported threat claim.");
            }

            return new ValidationResult(
                IsSafe: true,
                Answer: response.Answer,
                Reason: "Response passed Ask Sentinel final safety validation.");
        }

        private static ValidationResult Block(string reason) =>
            new(
                IsSafe: false,
                Answer: InsufficientEvidence,
                Reason: reason);

        private static bool ClaimsSuccessfulAction(string value) =>
            ContainsAny(value,
                "successfully fixed",
                "successfully removed",
                "successfully blocked",
                "successfully stopped",
                "successfully restarted",
                "has been fixed",
                "has been removed",
                "has been blocked",
                "has been resolved");

        private static bool ClaimsActionWasPerformed(string value) =>
            ContainsAny(value,
                "sentinel fixed",
                "sentinel removed",
                "sentinel blocked",
                "sentinel stopped",
                "sentinel restarted",
                "sentinel quarantined");

        private static bool ClaimsThreatFound(string value) =>
            ContainsAny(value,
                "sentinel found malware",
                "sentinel found a virus",
                "sentinel found a threat",
                "your computer is infected",
                "malware is present");

        private static bool ContainsAny(string value, params string[] terms)
        {
            foreach (string term in terms)
            {
                if (value.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public sealed record ValidationResult(
            bool IsSafe,
            string Answer,
            string Reason);
    }
}
