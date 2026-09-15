/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Decides whether a short Ask Sentinel message depends on the immediately
    /// preceding validated exchange. Conversation context is advisory memory only:
    /// it is never independent evidence of this computer's current state or proof
    /// that a security/remediation action occurred.
    /// </summary>
    public sealed class AskSentinelConversationContextPolicy
    {
        private static readonly TimeSpan MaximumContextAge = TimeSpan.FromMinutes(30);
        private const int MaximumCurrentQuestionCharacters = 220;
        private const int MaximumPreviousQuestionCharacters = 320;
        private const int MaximumPreviousAnswerCharacters = 520;

        public AskSentinelConversationContextDecision Decide(
            string currentQuestion,
            string? previousQuestion,
            string? previousAnswer,
            DateTimeOffset? previousAnswerUtc = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(currentQuestion);

            string current = currentQuestion.Trim();
            if (current.Contains("\nOriginal question: ", StringComparison.Ordinal))
            {
                return AskSentinelConversationContextDecision.NotUsed(
                    "The follow-up already contains its original question and does not need implicit conversation memory.");
            }

            if (AskSentinelRoutingPolicy.IsClearlyLocalStateQuestion(current))
            {
                return AskSentinelConversationContextDecision.NotUsed(
                    "The request clearly asks for fresh local computer state, so prior conversational wording is not needed.");
            }

            if (string.IsNullOrWhiteSpace(previousQuestion) || string.IsNullOrWhiteSpace(previousAnswer))
            {
                return AskSentinelConversationContextDecision.NotUsed(
                    "There is no prior validated Ask Sentinel exchange to resolve conversational references.");
            }

            if (previousAnswerUtc is DateTimeOffset timestamp &&
                DateTimeOffset.UtcNow - timestamp > MaximumContextAge)
            {
                return AskSentinelConversationContextDecision.NotUsed(
                    "The previous exchange is too old to use as implicit conversation context.");
            }

            if (!IsContextDependentFollowUp(current))
            {
                return AskSentinelConversationContextDecision.NotUsed(
                    "The new request is self-contained and should not inherit the prior exchange.");
            }

            string context =
                "Conversation context only; the prior answer is not independent evidence and may be stale. " +
                $"Previous user request: {Limit(previousQuestion!, MaximumPreviousQuestionCharacters)} " +
                $"Previous Sentinel answer: {Limit(previousAnswer!, MaximumPreviousAnswerCharacters)} " +
                $"Current follow-up: {Limit(current, MaximumCurrentQuestionCharacters)} " +
                "Use the prior exchange only to resolve references such as this/that/it/why/what-about. " +
                "Re-check every claim about this computer against the current verified evidence package. " +
                "Do not treat the prior answer as proof of current machine state or proof that any action completed.";

            return new AskSentinelConversationContextDecision(
                UsePriorExchange: true,
                SupplementalContext: context,
                Reason: "The short follow-up contains a conversational reference to the immediately preceding validated exchange.");
        }

        public static bool IsContextDependentFollowUp(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return false;
            string value = Normalize(question);
            if (value.Length > MaximumCurrentQuestionCharacters) return false;

            if (EqualsAny(value,
                "why", "why?", "how so", "how so?", "tell me more", "tell me more?",
                "explain more", "explain that", "go on", "continue", "then what", "then what?",
                "what next", "what next?", "what should i do", "what should i do?",
                "do i need to worry", "do i need to worry?", "is this bad", "is this bad?",
                "is that bad", "is that bad?", "is it safe", "is it safe?",
                "is that safe", "is that safe?", "would you recommend that", "would you recommend that?"))
                return true;

            return StartsWithAny(value,
                "what do you mean", "what does that mean", "what does it mean",
                "what about ", "how about ", "and ", "also ", "then ",
                "why is that", "why does that", "why did that", "why would that",
                "could that", "would that", "does that", "did that", "can that",
                "is that", "is this", "could it", "would it", "does it", "did it", "can it",
                "what if ", "can you explain that", "can you explain it",
                "should i do that", "should i do it", "do you recommend that");
        }

        private static string Normalize(string value) =>
            string.Join(' ', value.Trim().ToLowerInvariant().Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

        private static bool EqualsAny(string value, params string[] candidates) =>
            candidates.Any(candidate => value.Equals(candidate, StringComparison.OrdinalIgnoreCase));

        private static bool StartsWithAny(string value, params string[] prefixes) =>
            prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        private static string Limit(string value, int maximumCharacters)
        {
            string trimmed = value.Trim();
            return trimmed.Length <= maximumCharacters
                ? trimmed
                : trimmed[..maximumCharacters] + "…";
        }
    }

    public sealed record AskSentinelConversationContextDecision(
        bool UsePriorExchange,
        string SupplementalContext,
        string Reason)
    {
        public static AskSentinelConversationContextDecision NotUsed(string reason) =>
            new(false, string.Empty, reason);
    }

    /// <summary>
    /// Process-local, non-persistent conversation memory for the single Ask Sentinel
    /// interaction pipeline. Only answers that pass final display-safety validation
    /// are remembered. Nothing here is promoted to verified machine evidence.
    /// </summary>
    internal static class AskSentinelConversationContextStore
    {
        private static readonly object Gate = new();
        private static readonly AskSentinelConversationContextPolicy Policy = new();
        private static string _previousQuestion = string.Empty;
        private static string _previousAnswer = string.Empty;
        private static DateTimeOffset? _previousAnswerUtc;
        private static PendingQuestion _pending = PendingQuestion.Empty;

        public static void BeginQuestion(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return;

            lock (Gate)
            {
                AskSentinelConversationContextDecision decision = Policy.Decide(
                    question,
                    _previousQuestion,
                    _previousAnswer,
                    _previousAnswerUtc);

                _pending = new PendingQuestion(
                    question.Trim(),
                    decision.UsePriorExchange ? decision.SupplementalContext : string.Empty);
            }
        }

        public static string? GetSupplementalContext(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return null;

            lock (Gate)
            {
                if (!_pending.Question.Equals(question.Trim(), StringComparison.Ordinal)) return null;
                return string.IsNullOrWhiteSpace(_pending.SupplementalContext)
                    ? null
                    : _pending.SupplementalContext;
            }
        }

        public static void RememberValidatedAnswer(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer)) return;

            lock (Gate)
            {
                if (string.IsNullOrWhiteSpace(_pending.Question)) return;
                _previousQuestion = _pending.Question;
                _previousAnswer = answer.Trim();
                _previousAnswerUtc = DateTimeOffset.UtcNow;
                _pending = PendingQuestion.Empty;
            }
        }

        private sealed record PendingQuestion(string Question, string SupplementalContext)
        {
            public static PendingQuestion Empty { get; } = new(string.Empty, string.Empty);
        }
    }
}
