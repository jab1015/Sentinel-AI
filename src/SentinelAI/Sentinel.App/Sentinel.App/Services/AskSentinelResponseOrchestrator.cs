/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Coordinates Ask Sentinel responses through the verified local evidence context
    /// and the fail-closed local responder. The orchestration layer never adds facts
    /// that are not present in the supplied SystemSnapshot.
    /// </summary>
    public sealed class AskSentinelResponseOrchestrator
    {
        private const string InsufficientEvidence =
            "Sentinel does not yet have enough verified information to answer that question.";

        private readonly AskSentinelContextBuilder _contextBuilder = new();
        private readonly AskSentinelLocalResponder _localResponder = new();

        public AskSentinelResponse CreateResponse(string question, SystemSnapshot snapshot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            ArgumentNullException.ThrowIfNull(snapshot);

            AskSentinelContextBuilder.AskSentinelContext context = _contextBuilder.Build(snapshot);
            string answer = _localResponder.Answer(question, snapshot).Trim();

            if (string.IsNullOrWhiteSpace(answer))
            {
                answer = InsufficientEvidence;
            }

            bool insufficientEvidence = answer.Equals(InsufficientEvidence, StringComparison.OrdinalIgnoreCase) ||
                answer.Contains("does not currently have verified", StringComparison.OrdinalIgnoreCase) ||
                answer.Contains("does not yet have enough verified", StringComparison.OrdinalIgnoreCase);

            string groundingSummary = insufficientEvidence
                ? $"Sentinel checked {context.Evidence.Count} verified local evidence item(s) and did not find enough support for a factual answer."
                : $"Answer grounded in {context.Evidence.Count} verified local evidence item(s) from the current Sentinel snapshot.";

            return new AskSentinelResponse(
                Answer: answer,
                EvidenceTimestamp: snapshot.Timestamp,
                EvidenceCount: context.Evidence.Count,
                RequiresAttention: context.RequiresAttention,
                IsInsufficientEvidence: insufficientEvidence,
                GroundingSummary: groundingSummary);
        }

        public sealed record AskSentinelResponse(
            string Answer,
            DateTimeOffset EvidenceTimestamp,
            int EvidenceCount,
            bool RequiresAttention,
            bool IsInsufficientEvidence,
            string GroundingSummary);
    }
}
