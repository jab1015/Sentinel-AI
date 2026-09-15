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
    /// than requiring an exact phrase match. Clearly local state questions stay local;
    /// explicit current-source requests go to research; everything else uses Basic AI.
    /// </summary>
    public sealed class AskSentinelRoutingPolicy
    {
        public AskSentinelRoute Decide(string question, bool localAnswerInsufficient)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            string value = Normalize(question);

            bool freshResearch = RequiresFreshExternalResearch(value);
            bool explanation = NeedsNaturalLanguageExplanation(value);
            bool clearlyLocal = IsClearlyLocalStateQuestion(value);

            // This is intentionally default-to-AI rather than default-to-keywords.
            // Deterministic handlers are authoritative for clearly local state. They may
            // still provide useful evidence for every other question, but they must not
            // prevent natural-language reasoning merely because one broad keyword matched.
            bool useBasicAi = !freshResearch &&
                              (localAnswerInsufficient || explanation || !clearlyLocal);

            return new AskSentinelRoute(
                UseBasicAi: useBasicAi,
                UseExternalResearch: freshResearch,
                ExplanationRequested: explanation || (!clearlyLocal && !freshResearch),
                Reason: freshResearch
                    ? "The request depends on current or explicitly requested external information."
                    : clearlyLocal && !localAnswerInsufficient && !explanation
                        ? "Verified local evidence directly answers this computer-state question."
                        : localAnswerInsufficient
                            ? "Deterministic local evidence did not fully answer the request; use Basic AI."
                            : explanation
                                ? "The request asks for explanation or interpretation beyond a terse local status value."
                                : "The request is not clearly a local state query, so Basic AI is the default natural-language reasoning layer.");
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

            // Strong research intent always wins, even when the request also mentions
            // this PC (for example, "search current Microsoft guidance for my firewall").
            bool explicitExternalIntent = ContainsAny(value,
                "search online", "search the internet", "look online", "look it up", "external source", "external sources",
                "authoritative source", "authoritative sources", "official source", "official sources", "official documentation",
                "according to", "microsoft says", "vendor says", "manufacturer says", "release notes",
                "known issue", "known issues", "known cause", "known causes", "cve", "security advisory",
                "research this", "research the", "research my", "research whether", "research why", "research how",
                "check online", "check the internet", "web search", "search the web");
            if (explicitExternalIntent) return true;

            // Freshness words by themselves do not mean "go to the internet" when the
            // user is asking about this computer. "CPU right now" and "my latest local
            // status" must stay on the current verified snapshot.
            bool freshnessDependent = ContainsAny(value,
                "latest", "latest information", "today", "right now", "current version", "current release");
            return freshnessDependent && !IsClearlyLocalStateQuestion(value);
        }

        internal static bool NeedsNaturalLanguageExplanation(string question)
        {
            string value = Normalize(question);

            if (ContainsAny(value,
                "what does", "what do ", "how does", "how do ", "how can", "how should", "why is", "why does", "why did",
                "explain", "tell me about", "help me understand", "difference between", "what's the difference", "whats the difference",
                "meaning of", "what does it mean", "walk me through"))
                return true;

            bool definitionRequest = StartsWithAny(value,
                "what is ", "what are ", "who is ", "who are ");
            if (!definitionRequest) return false;

            return !IsClearlyLocalStateQuestion(value);
        }

        internal static bool IsClearlyLocalStateQuestion(string question)
        {
            string value = Normalize(question);

            if (ContainsAny(value,
                "my computer", "my pc", "this computer", "this pc", "on this computer", "on this pc",
                "on my computer", "on my pc", "current status", "status of my", "status on this",
                "right now", "currently", "what is my ", "what are my ", "what's my ", "whats my ",
                "using now", "using right now", "usage now", "usage right now", "installed on", "running on",
                "enabled on", "disabled on", "flagged on", "detected on", "found on"))
                return true;

            if (ContainsAny(value,
                "is defender on", "is defender enabled", "defender status",
                "is firewall on", "is firewall enabled", "firewall status",
                "is bitlocker on", "is bitlocker enabled", "bitlocker status",
                "is secure boot on", "is secure boot enabled", "secure boot status",
                "is tpm on", "is tpm enabled", "tpm status",
                "windows update status", "check for updates", "pending restart", "restart pending",
                "cpu usage", "memory usage", "ram usage", "disk usage", "storage usage",
                "running processes", "running services", "startup apps", "scheduled tasks"))
                return true;

            // Very short topic prompts are useful Sentinel shorthand for local status.
            // Explicit definitions are never treated as that shorthand.
            bool explicitDefinition = StartsWithAny(value,
                "what is ", "what are ", "what does ", "how does ", "who is ", "who are ");
            string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!explicitDefinition && words.Length <= 3 && ContainsAny(value,
                "cpu", "memory", "ram", "disk", "storage", "defender", "firewall", "bitlocker", "secure boot",
                "tpm", "windows update", "drivers", "driver", "startup apps", "services", "processes", "network"))
                return true;

            return false;
        }

        private static bool StartsWithAny(string value, params string[] prefixes) =>
            prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

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
