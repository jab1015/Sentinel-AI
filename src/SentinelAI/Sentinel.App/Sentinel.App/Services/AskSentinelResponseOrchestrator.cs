/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Coordinates Ask Sentinel responses through verified current evidence and,
    /// when the user explicitly asks about prior occurrences, persisted Sentinel
    /// investigation history. The orchestration layer never adds unsupported facts.
    /// </summary>
    public sealed class AskSentinelResponseOrchestrator
    {
        private const string InsufficientEvidence =
            "Sentinel does not yet have enough verified information to answer that question.";

        private readonly AskSentinelContextBuilder _contextBuilder = new();
        private readonly AskSentinelLocalResponder _localResponder = new();

        public AskSentinelResponse CreateResponse(
            string question,
            SystemSnapshot snapshot,
            IReadOnlyList<InvestigationHistoryService.InvestigationHistoryEntry>? history = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            ArgumentNullException.ThrowIfNull(snapshot);

            AskSentinelContextBuilder.AskSentinelContext context = _contextBuilder.Build(snapshot);
            string answer;
            bool usedHistory = false;

            if (IsHistoryQuestion(question))
            {
                answer = CreateHistoryAnswer(snapshot, history ?? Array.Empty<InvestigationHistoryService.InvestigationHistoryEntry>());
                usedHistory = !answer.Equals(InsufficientEvidence, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                answer = _localResponder.Answer(question, snapshot).Trim();
            }

            if (string.IsNullOrWhiteSpace(answer))
            {
                answer = InsufficientEvidence;
            }

            bool insufficientEvidence = answer.Equals(InsufficientEvidence, StringComparison.OrdinalIgnoreCase) ||
                answer.Contains("does not currently have verified", StringComparison.OrdinalIgnoreCase) ||
                answer.Contains("does not yet have enough verified", StringComparison.OrdinalIgnoreCase);

            string groundingSummary;
            if (usedHistory)
            {
                groundingSummary = "Answer grounded in Sentinel's persisted investigation history and the current verified snapshot.";
            }
            else if (insufficientEvidence)
            {
                groundingSummary = $"Sentinel checked {context.Evidence.Count} verified local evidence item(s) and did not find enough support for a factual answer.";
            }
            else
            {
                groundingSummary = $"Answer grounded in {context.Evidence.Count} verified local evidence item(s) from the current Sentinel snapshot.";
            }

            return new AskSentinelResponse(
                Answer: answer,
                EvidenceTimestamp: snapshot.Timestamp,
                EvidenceCount: context.Evidence.Count,
                RequiresAttention: context.RequiresAttention,
                IsInsufficientEvidence: insufficientEvidence,
                UsedInvestigationHistory: usedHistory,
                GroundingSummary: groundingSummary);
        }

        private static bool IsHistoryQuestion(string question)
        {
            string value = question.Trim().ToLowerInvariant();
            return value.Contains("before") ||
                   value.Contains("previous") ||
                   value.Contains("previously") ||
                   value.Contains("history") ||
                   value.Contains("again") ||
                   value.Contains("last time") ||
                   value.Contains("past") ||
                   value.Contains("earlier");
        }

        private static string CreateHistoryAnswer(
            SystemSnapshot snapshot,
            IReadOnlyList<InvestigationHistoryService.InvestigationHistoryEntry> history)
        {
            if (history.Count == 0)
            {
                return "Sentinel does not have a verified prior investigation in local history that supports an answer to that question.";
            }

            string fingerprint = snapshot.InvestigationReasonCode?.Trim() ?? string.Empty;
            InvestigationHistoryService.InvestigationHistoryEntry? matching = null;

            if (!string.IsNullOrWhiteSpace(fingerprint) &&
                !fingerprint.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                !fingerprint.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
            {
                matching = history.FirstOrDefault(entry =>
                    string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            }

            if (matching is not null)
            {
                string conclusion = string.IsNullOrWhiteSpace(matching.Conclusion)
                    ? "No additional conclusion was recorded."
                    : matching.Conclusion;

                string outcome = matching.Resolved
                    ? "That recorded occurrence was later marked resolved."
                    : matching.RequiresAttention
                        ? "That recorded occurrence required attention."
                        : "That recorded occurrence did not require attention.";

                return $"Sentinel has seen this same verified condition before. It was recorded {matching.TimestampUtc.ToLocalTime():MMM d, yyyy h:mm tt}. {conclusion} {outcome}";
            }

            InvestigationHistoryService.InvestigationHistoryEntry latest = history[0];
            string latestConclusion = string.IsNullOrWhiteSpace(latest.Conclusion)
                ? "No additional conclusion was recorded."
                : latest.Conclusion;

            return $"Sentinel has prior verified investigation history, but it does not establish that the current condition is the same. The most recent recorded investigation was {latest.TimestampUtc.ToLocalTime():MMM d, yyyy h:mm tt}: {latestConclusion}";
        }

        public sealed record AskSentinelResponse(
            string Answer,
            DateTimeOffset EvidenceTimestamp,
            int EvidenceCount,
            bool RequiresAttention,
            bool IsInsufficientEvidence,
            bool UsedInvestigationHistory,
            string GroundingSummary);
    }
}
