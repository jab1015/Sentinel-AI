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
    public sealed class AskSentinelResponseOrchestrator
    {
        private const string InsufficientEvidence =
            "Sentinel does not yet have enough verified information to answer that question.";

        private readonly AskSentinelContextBuilder _contextBuilder = new();
        private readonly AskSentinelLocalResponder _localResponder = new();
        private readonly AskSentinelRecommendationAdvisor _recommendationAdvisor = new();
        private readonly AskSentinelResponseSafetyValidator _safetyValidator = new();

        public AskSentinelResponse CreateResponse(string question, SystemSnapshot snapshot,
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
                var recommendation = _recommendationAdvisor.CreateRecommendation(snapshot);
                answer = recommendation.Answer;
                usedRecommendationGuard = true;
            }
            else answer = _localResponder.Answer(question, snapshot).Trim();

            if (string.IsNullOrWhiteSpace(answer)) answer = InsufficientEvidence;
            bool insufficientEvidence = IsInsufficientEvidence(answer);

            AskSentinelResponse preliminary = new(answer, snapshot.Timestamp, context.Evidence.Count,
                context.RequiresAttention, insufficientEvidence, usedHistory, usedRecommendationGuard, false,
                BuildGroundingSummary(context, usedHistory, usedRecommendationGuard, insufficientEvidence));

            var validation = _safetyValidator.Validate(preliminary, snapshot);
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

        private static string CreateHistoryAnswer(string question, SystemSnapshot snapshot,
            IReadOnlyList<InvestigationHistoryService.InvestigationHistoryEntry> history)
        {
            if (history.Count == 0)
                return "I don't have a previous verified investigation that answers that question.";

            var matching = FindQuestionTopicMatch(question, history);
            if (matching is null)
            {
                string fingerprint = snapshot.InvestigationReasonCode?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(fingerprint) &&
                    !fingerprint.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                    !fingerprint.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
                    matching = history.FirstOrDefault(e => string.Equals(e.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            }

            if (matching is null)
                return "I have previous investigation history, but nothing that matches this question closely enough to rely on.";

            string finding = CreateFriendlyHistoryFinding(question, matching);
            string currentState = CreateCurrentHistoryState(question, snapshot, matching);

            return $"Last time I checked:\n\n{finding}\n\nCurrent status: {currentState}\n\nChecked {matching.TimestampUtc.ToLocalTime():MMM d, yyyy h:mm tt}.";
        }

        private static string CreateFriendlyHistoryFinding(string question,
            InvestigationHistoryService.InvestigationHistoryEntry entry)
        {
            string q = question.ToLowerInvariant();
            string conclusion = entry.Conclusion ?? string.Empty;

            if (q.Contains("driver") || EntryContains(entry, "driver"))
            {
                string source = conclusion.Contains("Dell Support", StringComparison.OrdinalIgnoreCase)
                    ? "Dell Support"
                    : conclusion.Contains("Intel", StringComparison.OrdinalIgnoreCase)
                        ? "the hardware manufacturer's support site"
                        : "the computer manufacturer's support site";

                return $"I found a problem with one of your computer's drivers. I couldn't verify a safe automatic repair, so I identified {source} as the correct next source.";
            }

            string simplified = SimplifyConclusion(conclusion);
            return string.IsNullOrWhiteSpace(simplified)
                ? "I completed the investigation but did not record an additional user-facing finding."
                : simplified;
        }

        private static string CreateCurrentHistoryState(string question, SystemSnapshot snapshot,
            InvestigationHistoryService.InvestigationHistoryEntry entry)
        {
            string q = question.ToLowerInvariant();

            if (q.Contains("driver") || EntryContains(entry, "driver"))
            {
                bool currentDriverCondition =
                    snapshot.InvestigationRequiresAttention &&
                    (Contains(snapshot.InvestigationReasonCode, "driver") ||
                     Contains(snapshot.InvestigationConclusion, "driver") ||
                     Contains(snapshot.InvestigationSummary, "driver") ||
                     Contains(snapshot.GuidanceTitle, "driver") ||
                     Contains(snapshot.GuidanceWhatHappened, "driver"));

                return currentDriverCondition
                    ? "The driver problem is still being reported."
                    : "The driver problem is not currently being reported.";
            }

            if (entry.Resolved)
                return "The recorded issue was later marked resolved.";

            if (snapshot.InvestigationRequiresAttention)
                return "Sentinel is currently reporting an issue that needs attention.";

            return "Sentinel is not currently reporting this issue.";
        }

        private static bool Contains(string? value, string term) =>
            !string.IsNullOrWhiteSpace(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

        private static string SimplifyConclusion(string conclusion)
        {
            string value = conclusion.Trim();
            if (value.StartsWith("Sentinel ", StringComparison.OrdinalIgnoreCase))
                value = "I " + value[9..];

            value = value.Replace("Sentinel's", "my", StringComparison.OrdinalIgnoreCase)
                         .Replace("Sentinel", "I", StringComparison.OrdinalIgnoreCase);
            return value;
        }

        private static InvestigationHistoryService.InvestigationHistoryEntry? FindQuestionTopicMatch(string question,
            IReadOnlyList<InvestigationHistoryService.InvestigationHistoryEntry> history)
        {
            string q = question.ToLowerInvariant();
            if (q.Contains("driver")) return history.FirstOrDefault(e => EntryContains(e, "driver"));
            if (q.Contains("network") || q.Contains("internet") || q.Contains("connection"))
                return history.FirstOrDefault(e => EntryContains(e, "network") || EntryContains(e, "internet") || EntryContains(e, "connection"));
            if (q.Contains("firewall")) return history.FirstOrDefault(e => EntryContains(e, "firewall"));
            if (q.Contains("process") || q.Contains("spyware"))
                return history.FirstOrDefault(e => EntryContains(e, "process") || EntryContains(e, "spyware"));
            if (q.Contains("update")) return history.FirstOrDefault(e => EntryContains(e, "update"));
            return null;
        }

        private static bool EntryContains(InvestigationHistoryService.InvestigationHistoryEntry entry, string term) =>
            entry.Fingerprint.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Conclusion.Contains(term, StringComparison.OrdinalIgnoreCase);

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

        private static bool IsInsufficientEvidence(string answer) =>
            answer.Equals(InsufficientEvidence, StringComparison.OrdinalIgnoreCase) ||
            answer.Contains("does not currently have verified", StringComparison.OrdinalIgnoreCase) ||
            answer.Contains("does not yet have enough verified", StringComparison.OrdinalIgnoreCase) ||
            answer.Contains("does not have a verified prior investigation", StringComparison.OrdinalIgnoreCase);

        private static string BuildGroundingSummary(AskSentinelContextBuilder.AskSentinelContext context,
            bool usedHistory, bool usedRecommendationGuard, bool insufficientEvidence)
        {
            if (usedHistory) return "Answer grounded in Sentinel's persisted investigation history and the current verified snapshot.";
            if (usedRecommendationGuard) return "Recommendation grounded in current verified Sentinel evidence and remediation safety state; no unverified action or outcome is claimed.";
            if (insufficientEvidence) return $"Sentinel checked {context.Evidence.Count} verified local evidence item(s) and did not find enough support for a factual answer.";
            return $"Answer grounded in {context.Evidence.Count} verified local evidence item(s) from the current Sentinel snapshot.";
        }

        public sealed record AskSentinelResponse(string Answer, DateTimeOffset EvidenceTimestamp, int EvidenceCount,
            bool RequiresAttention, bool IsInsufficientEvidence, bool UsedInvestigationHistory,
            bool UsedRecommendationGuard, bool PassedFinalSafetyValidation, string GroundingSummary);
    }
}
