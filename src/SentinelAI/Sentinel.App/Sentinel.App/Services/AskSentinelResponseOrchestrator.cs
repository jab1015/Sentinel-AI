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
        private readonly MaintenanceHistoryService _maintenanceHistoryService = new();

        public AskSentinelResponse CreateResponse(string question, SystemSnapshot snapshot,
            IReadOnlyList<InvestigationHistoryService.InvestigationHistoryEntry>? history = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            ArgumentNullException.ThrowIfNull(snapshot);

            AskSentinelContextBuilder.AskSentinelContext context = _contextBuilder.Build(snapshot);
            string answer;
            bool usedHistory = false;
            bool usedRecommendationGuard = false;

            if (IsMaintenanceHistoryQuestion(question))
            {
                answer = CreateMaintenanceHistoryAnswer(question, snapshot, _maintenanceHistoryService.GetSummary());
                usedHistory = true;
            }
            else if (IsHistoryQuestion(question))
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

            bool localInsufficient = IsInsufficientEvidence(answer);
            bool requiresExternalKnowledge = !usedHistory && RequiresExternalKnowledge(question, answer);
            bool insufficientEvidence = localInsufficient || requiresExternalKnowledge;

            AskSentinelResponse preliminary = new(answer, snapshot.Timestamp, context.Evidence.Count,
                context.RequiresAttention, insufficientEvidence, usedHistory, usedRecommendationGuard, false,
                BuildGroundingSummary(context, usedHistory, usedRecommendationGuard, localInsufficient, requiresExternalKnowledge));

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

        private static bool RequiresExternalKnowledge(string question, string localAnswer)
        {
            string value = question.Trim().ToLowerInvariant();

            string[] explicitExternalIntent =
            {
                "external source", "external sources", "authoritative source", "authoritative sources",
                "according to", "look online", "search online", "search the internet", "check the internet",
                "research", "known cause", "known causes", "microsoft says", "vendor says",
                "manufacturer says", "official documentation", "latest information"
            };

            if (explicitExternalIntent.Any(value.Contains)) return true;

            bool asksForInterpretation =
                value.Contains("what does") || value.Contains("what does this mean") ||
                value.Contains("why is") || value.Contains("why does") || value.Contains("why did") ||
                value.Contains("root cause") || value.Contains("cause of") || value.Contains("causes of") ||
                value.Contains("which cause") || value.Contains("best matches") || value.Contains("explain");

            if (!asksForInterpretation) return false;

            string answer = localAnswer?.Trim() ?? string.Empty;
            if (answer.Length < 420) return true;

            bool containsCausalExplanation =
                answer.Contains("because", StringComparison.OrdinalIgnoreCase) ||
                answer.Contains("caused by", StringComparison.OrdinalIgnoreCase) ||
                answer.Contains("reason", StringComparison.OrdinalIgnoreCase) ||
                answer.Contains("evidence shows", StringComparison.OrdinalIgnoreCase);

            return !containsCausalExplanation;
        }

        private static string CreateMaintenanceHistoryAnswer(string question, SystemSnapshot snapshot, MaintenanceHistorySummary summary)
        {
            string value = question.Trim().ToLowerInvariant();
            bool optimizationOnly = value.Contains("optimization") || value.Contains("optimizations") ||
                                    value.Contains("optimize") || value.Contains("optimized");

            MaintenanceHistoryEntry[] entries = summary.Entries
                .Where(entry => !optimizationOnly || entry.Category.Equals("Optimization", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.TimestampUtc)
                .ToArray();

            if (entries.Length == 0)
            {
                if (!optimizationOnly)
                    return "I don't have a verified record of any maintenance actions being performed on this computer in the last 30 days.";

                string historical = summary.TotalActions > 0
                    ? $"Sentinel has {summary.TotalActions} verified maintenance record{(summary.TotalActions == 1 ? string.Empty : "s")} in the last 30 days, but none is categorized as a performance optimization action."
                    : "Sentinel has no recorded performance optimization actions in the last 30 days.";
                return $"{historical}\n\nCurrent status: {CreateCurrentOptimizationStatus(snapshot)}";
            }

            string[] recent = entries.Take(5).Select(entry =>
            {
                string outcome = entry.RolledBack
                    ? "rolled back safely"
                    : entry.Verified
                        ? "verified"
                        : entry.Successful
                            ? "completed"
                            : "needs follow-up";
                return $"• {entry.Action} — {entry.UserSummary} ({outcome}, {entry.TimestampUtc.ToLocalTime():MMM d, h:mm tt})";
            }).ToArray();

            string heading = optimizationOnly
                ? $"I found {entries.Length} recorded optimization action{(entries.Length == 1 ? string.Empty : "s")} in the last 30 days."
                : $"I found {entries.Length} recorded maintenance action{(entries.Length == 1 ? string.Empty : "s")} in the last 30 days.";

            return $"{heading}\n\n{string.Join("\n", recent)}" +
                   (entries.Length > recent.Length ? $"\n\nShowing the {recent.Length} most recent." : string.Empty) +
                   $"\n\nCurrent status: {CreateCurrentOptimizationStatus(snapshot)}";
        }

        private static string CreateCurrentOptimizationStatus(SystemSnapshot snapshot) =>
            IsVerifiedOptimalPerformance(snapshot)
                ? "Sentinel's current verified evaluation says no performance optimization is needed right now."
                : "Sentinel is continuing to evaluate whether performance optimization is currently needed; the historical record alone does not establish current need.";

        private static bool IsVerifiedOptimalPerformance(SystemSnapshot snapshot)
        {
            string combined = string.Join(" ", new[]
            {
                snapshot.InvestigationSummary ?? string.Empty,
                snapshot.InvestigationConclusion ?? string.Empty,
                snapshot.GuidanceTitle ?? string.Empty,
                snapshot.GuidanceWhatHappened ?? string.Empty,
                snapshot.GuidanceEvidence ?? string.Empty
            });

            return combined.Contains("no verified performance optimization is needed", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("performance is within this computer's established baseline", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("optimization check complete", StringComparison.OrdinalIgnoreCase);
        }

        private static string CreateHistoryAnswer(string question, SystemSnapshot snapshot,
            IReadOnlyList<InvestigationHistoryService.InvestigationHistoryEntry> history)
        {
            if (history.Count == 0)
                return "I don't have a previous verified investigation that answers that question.";

            var matching = FindQuestionTopicMatch(question, history);
            if (matching is null && IsGenericIssueHistoryQuestion(question))
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

        private static bool IsMaintenanceHistoryQuestion(string question)
        {
            string value = question.Trim().ToLowerInvariant();
            bool maintenanceTopic = value.Contains("optimization") || value.Contains("optimizations") ||
                                    value.Contains("optimize") || value.Contains("optimized") ||
                                    value.Contains("maintenance") || value.Contains("cleanup") ||
                                    value.Contains("cleaned") || value.Contains("defrag") ||
                                    value.Contains("retrim") || value.Contains("startup optimization");
            bool historicalIntent = value.Contains("recent") || value.Contains("recently") ||
                                    value.Contains("lately") || value.Contains("done") ||
                                    value.Contains("performed") || value.Contains("have been") ||
                                    value.Contains("last") || value.Contains("history") ||
                                    value.Contains("what optimizations") || value.Contains("what maintenance");
            return maintenanceTopic && historicalIntent;
        }

        private static bool IsHistoryQuestion(string question)
        {
            string value = question.Trim().ToLowerInvariant();
            return value.Contains("before") || value.Contains("previous") || value.Contains("previously") ||
                   value.Contains("history") || value.Contains("again") || value.Contains("last time") ||
                   value.Contains("past") || value.Contains("earlier") || value.Contains("recently") ||
                   value.Contains("lately");
        }

        private static bool IsGenericIssueHistoryQuestion(string question)
        {
            string value = question.Trim().ToLowerInvariant();
            bool hasExplicitTopic = value.Contains("driver") || value.Contains("network") ||
                                    value.Contains("internet") || value.Contains("connection") ||
                                    value.Contains("firewall") || value.Contains("process") ||
                                    value.Contains("spyware") || value.Contains("update") ||
                                    value.Contains("optimization") || value.Contains("maintenance");
            if (hasExplicitTopic) return false;

            return value.Contains("issue") || value.Contains("problem") || value.Contains("that") ||
                   value.Contains("this") || value.Contains("it") || value.Contains("last time");
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
            bool usedHistory, bool usedRecommendationGuard, bool localInsufficient, bool requiresExternalKnowledge)
        {
            if (usedHistory) return "Answer grounded in Sentinel's persisted investigation or maintenance history and the current verified snapshot.";
            if (usedRecommendationGuard && !requiresExternalKnowledge) return "Recommendation grounded in current verified Sentinel evidence and remediation safety state; no unverified action or outcome is claimed.";
            if (requiresExternalKnowledge) return $"Sentinel has {context.Evidence.Count} verified local evidence item(s), but the question asks for explanation, causal interpretation, or external knowledge that local evidence alone cannot fully establish. External investigation is required before the answer is complete.";
            if (localInsufficient) return $"Sentinel checked {context.Evidence.Count} verified local evidence item(s) and did not find enough support for a factual answer.";
            return $"Answer grounded in {context.Evidence.Count} verified local evidence item(s) from the current Sentinel snapshot.";
        }

        public sealed record AskSentinelResponse(string Answer, DateTimeOffset EvidenceTimestamp, int EvidenceCount,
            bool RequiresAttention, bool IsInsufficientEvidence, bool UsedInvestigationHistory,
            bool UsedRecommendationGuard, bool PassedFinalSafetyValidation, string GroundingSummary);
    }
}
