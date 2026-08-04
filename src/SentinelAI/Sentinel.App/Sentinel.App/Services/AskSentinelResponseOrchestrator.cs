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
    /// when appropriate, persisted investigation history or safeguarded recommendation
    /// logic. Every response passes a final fail-safe validation before presentation.
    /// </summary>
    public sealed class AskSentinelResponseOrchestrator
    {
        private const string InsufficientEvidence =
            "Sentinel does not yet have enough verified information to answer that question.";

        private readonly AskSentinelContextBuilder _contextBuilder = new();
        private readonly AskSentinelLocalResponder _localResponder = new();
        private readonly AskSentinelRecommendationAdvisor _recommendationAdvisor = new();
        private readonly AskSentinelResponseSafetyValidator _safetyValidator = new();

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
            bool usedRecommendationGuard = false;

            if (IsHistoryQuestion(question))
            {
                answer = CreateHistoryAnswer(question, snapshot, history ?? Array.Empty<InvestigationHistoryService.InvestigationHistoryEntry>());
                usedHistory = !answer.Equals(InsufficientEvidence, StringComparison.OrdinalIgnoreCase) &&
                              !answer.Contains("does not have a verified prior investigation", StringComparison.OrdinalIgnoreCase);
            }
            else if (IsRecommendationQuestion(question))
            {
                AskSentinelRecommendationAdvisor.RecommendationResult recommendation =
                    _recommendationAdvisor.CreateRecommendation(snapshot);
                answer = recommendation.Answer;
                usedRecommendationGuard = true;
            }
            else
            {
                answer = _localResponder.Answer(question, snapshot).Trim();
            }

            if (string.IsNullOrWhiteSpace(answer))
                answer = InsufficientEvidence;

            bool insufficientEvidence = IsInsufficientEvidence(answer);

            AskSentinelResponse preliminary = new(
                Answer: answer,
                EvidenceTimestamp: snapshot.Timestamp,
                EvidenceCount: context.Evidence.Count,
                RequiresAttention: context.RequiresAttention,
                IsInsufficientEvidence: insufficientEvidence,
                UsedInvestigationHistory: usedHistory,
                UsedRecommendationGuard: usedRecommendationGuard,
                PassedFinalSafetyValidation: false,
                GroundingSummary: BuildGroundingSummary(context, usedHistory, usedRecommendationGuard, insufficientEvidence));

            AskSentinelResponseSafetyValidator.ValidationResult validation =
                _safetyValidator.Validate(preliminary, snapshot);

            if (!validation.IsSafe)
            {
                return preliminary with
                {
                    Answer = validation.Answer,
                    IsInsufficientEvidence = true,
                    UsedInvestigationHistory = false,
                    UsedRecommendationGuard = false,
                    PassedFinalSafetyValidation = true,
                    GroundingSummary = "Sentinel blocked an unsupported or internally inconsistent response and returned an insufficient-evidence answer instead."
                };
            }

            return preliminary with { PassedFinalSafetyValidation = true };
        }

        private static string BuildGroundingSummary(
            AskSentinelContextBuilder.AskSentinelContext context,
            bool usedHistory,
            bool usedRecommendationGuard,
            bool insufficientEvidence)
        {
            if (usedHistory)
                return "Answer grounded in Sentinel's persisted investigation history and the current verified snapshot.";

            if (usedRecommendationGuard)
                return "Recommendation grounded in current verified Sentinel evidence and remediation safety state; no unverified action or outcome is claimed.";

            if (insufficientEvidence)
                return $"Sentinel checked {context.Evidence.Count} verified local evidence item(s) and did not find enough support for a factual answer.";

            return $"Answer grounded in {context.Evidence.Count} verified local evidence item(s) from the current Sentinel snapshot.";
        }

        private static bool IsInsufficientEvidence(string answer) =>
            answer.Equals(InsufficientEvidence, StringComparison.OrdinalIgnoreCase) ||
            answer.Contains("does not currently have verified", StringComparison.OrdinalIgnoreCase) ||
            answer.Contains("does not yet have enough verified", StringComparison.OrdinalIgnoreCase) ||
            answer.Contains("does not have a verified prior investigation", StringComparison.OrdinalIgnoreCase);

        private static bool IsHistoryQuestion(string question)
        {
            string value = question.Trim().ToLowerInvariant();
            return value.Contains("before") || value.Contains("previous") || value.Contains("previously") ||
                   value.Contains("history") || value.Contains("again") || value.Contains("last time") ||
                   value.Contains("past") || value.Contains("earlier");
        }

        private static bool IsRecommendationQuestion(string question)
        {
            string value = question.Trim().ToLowerInvariant();
            return value.Contains("recommend") || value.Contains("should i") || value.Contains("what should") ||
                   value.Contains("what do i do") || value.Contains("what can i do") || value.Contains("how do i fix") ||
                   value.Contains("how should") || value.Contains("fix this") || value.Contains("do about");
        }

        private static string CreateHistoryAnswer(
            string question,
            SystemSnapshot snapshot,
            IReadOnlyList<InvestigationHistoryService.InvestigationHistoryEntry> history)
        {
            if (history.Count == 0)
                return "Sentinel does not have a verified prior investigation in local history that supports an answer to that question.";

            InvestigationHistoryService.InvestigationHistoryEntry? matching = FindQuestionTopicMatch(question, history);

            if (matching is null)
            {
                string fingerprint = snapshot.InvestigationReasonCode?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(fingerprint) &&
                    !fingerprint.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                    !fingerprint.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
                {
                    matching = history.FirstOrDefault(entry =>
                        string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
                }
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

                return $"Sentinel found a verified prior investigation that matches your question. It was recorded {matching.TimestampUtc.ToLocalTime():MMM d, yyyy h:mm tt}. {conclusion} {outcome}";
            }

            return "Sentinel has verified investigation history, but none of the stored findings match the subject of that question closely enough to use safely.";
        }

        private static InvestigationHistoryService.InvestigationHistoryEntry? FindQuestionTopicMatch(
            string question,
            IReadOnlyList<InvestigationHistoryService.InvestigationHistoryEntry> history)
        {
            string q = question.ToLowerInvariant();

            if (q.Contains("driver"))
                return history.FirstOrDefault(entry => EntryContains(entry, "driver"));

            if (q.Contains("network") || q.Contains("internet") || q.Contains("connection"))
                return history.FirstOrDefault(entry => EntryContains(entry, "network") || EntryContains(entry, "internet") || EntryContains(entry, "connection"));

            if (q.Contains("firewall"))
                return history.FirstOrDefault(entry => EntryContains(entry, "firewall"));

            if (q.Contains("process") || q.Contains("spyware"))
                return history.FirstOrDefault(entry => EntryContains(entry, "process") || EntryContains(entry, "spyware"));

            if (q.Contains("update"))
                return history.FirstOrDefault(entry => EntryContains(entry, "update"));

            return null;
        }

        private static bool EntryContains(InvestigationHistoryService.InvestigationHistoryEntry entry, string term) =>
            entry.Fingerprint.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Conclusion.Contains(term, StringComparison.OrdinalIgnoreCase);

        public sealed record AskSentinelResponse(
            string Answer,
            DateTimeOffset EvidenceTimestamp,
            int EvidenceCount,
            bool RequiresAttention,
            bool IsInsufficientEvidence,
            bool UsedInvestigationHistory,
            bool UsedRecommendationGuard,
            bool PassedFinalSafetyValidation,
            string GroundingSummary);
    }
}
