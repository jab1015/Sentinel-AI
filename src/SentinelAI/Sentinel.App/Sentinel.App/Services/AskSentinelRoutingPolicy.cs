/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Chooses the next Ask Sentinel reasoning layer from the user's intent rather
    /// than requiring an exact phrase match. Local deterministic evidence stays first;
    /// Basic AI is the normal language fallback; fresh/current claims go to research.
    /// </summary>
    public sealed class AskSentinelRoutingPolicy
    {
        public AskSentinelRoute Decide(string question, bool localAnswerInsufficient)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            string value = Normalize(question);

            bool freshResearch = RequiresFreshExternalResearch(value);
            bool explanation = NeedsNaturalLanguageExplanation(value);
            bool useBasicAi = !freshResearch && (localAnswerInsufficient || explanation);

            return new AskSentinelRoute(
                UseBasicAi: useBasicAi,
                UseExternalResearch: freshResearch,
                ExplanationRequested: explanation,
                Reason: freshResearch
                    ? "The question depends on current or explicitly requested external information."
                    : localAnswerInsufficient
                        ? "Deterministic local handlers did not fully answer the question; use Basic AI."
                        : explanation
                            ? "The user asked for explanation or interpretation beyond a terse local status value."
                            : "Verified local evidence directly answers the question.");
        }

        public AiEscalationContext CreateBasicAiContext(string question)
        {
            string value = Normalize(question);
            bool highRisk = ContainsAny(value,
                "security", "malware", "virus", "ransomware", "spyware", "hack", "breach",
                "firewall", "credential", "password", "phishing", "rootkit", "trojan");
            bool highComplexity = question.Length > 180 || ContainsAny(value,
                "compare", "analyze", "root cause", "why", "explain", "multiple", "several", "walk me through");

            return new AiEscalationContext(
                LocalEvidenceAvailable: true,
                LocalEvidenceInsufficient: true,
                LocalConclusionVerified: false,
                CachedVerifiedFindingAvailable: false,
                ExternalResearchApplicable: false,
                AuthoritativeResearchAttempted: false,
                AuthoritativeExternalConclusionVerified: false,
                NeedsInterpretation: true,
                NeedsUserExplanation: true,
                HighComplexity: highComplexity,
                HighRisk: highRisk);
        }

        public AiEscalationContext CreateExternalAiContext(
            string question,
            string topic,
            int sourceCount,
            bool authoritativeConclusionVerified)
        {
            string value = Normalize(question);
            bool highRisk = topic.Equals("security", StringComparison.OrdinalIgnoreCase) ||
                            topic.Equals("firewall", StringComparison.OrdinalIgnoreCase) ||
                            ContainsAny(value, "malware", "virus", "ransomware", "breach", "hack", "credential", "phishing");

            return new AiEscalationContext(
                LocalEvidenceAvailable: true,
                LocalEvidenceInsufficient: true,
                LocalConclusionVerified: false,
                CachedVerifiedFindingAvailable: false,
                ExternalResearchApplicable: true,
                AuthoritativeResearchAttempted: true,
                AuthoritativeExternalConclusionVerified: authoritativeConclusionVerified,
                NeedsInterpretation: true,
                NeedsUserExplanation: true,
                HighComplexity: sourceCount > 1 || question.Length > 180,
                HighRisk: highRisk);
        }

        public static bool RequiresFreshExternalResearch(string question)
        {
            string value = Normalize(question);
            return ContainsAny(value,
                "search online", "search the internet", "look online", "look it up", "external source", "external sources",
                "authoritative source", "official source", "official documentation", "microsoft says", "vendor says", "manufacturer says",
                "latest", "today", "right now", "current version", "current release", "release notes", "known issue", "known issues",
                "cve", "security advisory", "research this", "check online", "check the internet", "web search", "search the web");
        }

        internal static bool NeedsNaturalLanguageExplanation(string question)
        {
            string value = Normalize(question);

            if (ContainsAny(value,
                "what does", "what do ", "how does", "how do ", "how can", "how should", "why is", "why does", "why did",
                "explain", "tell me about", "help me understand", "difference between", "what's the difference", "whats the difference",
                "meaning of", "what does it mean", "walk me through"))
                return true;

            bool definitionQuestion = value.StartsWith("what is ", StringComparison.Ordinal) ||
                                      value.StartsWith("what are ", StringComparison.Ordinal) ||
                                      value.StartsWith("who is ", StringComparison.Ordinal) ||
                                      value.StartsWith("who are ", StringComparison.Ordinal);
            if (!definitionQuestion) return false;

            // Preserve fast deterministic answers for direct questions about this PC's
            // current state while still letting definitions such as "What is TPM?" use AI.
            return !ContainsAny(value,
                "what is my ", "what are my ", "on my computer", "on my pc", "for my computer", "for my pc",
                "current status", "status of my", "using right now", "usage right now", "going on with my");
        }

        private static string Normalize(string value) =>
            string.Join(' ', value.Trim().ToLowerInvariant().Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

        private static bool ContainsAny(string value, params string[] terms) =>
            terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public sealed record AskSentinelRoute(
        bool UseBasicAi,
        bool UseExternalResearch,
        bool ExplanationRequested,
        string Reason);
}
