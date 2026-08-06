/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Product-wide policy for deciding whether paid/cloud AI is warranted.
    /// Deterministic local intelligence and authoritative research always run first.
    /// </summary>
    public sealed class AiEscalationPolicy
    {
        public AiEscalationDecision Evaluate(AiEscalationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.LocalConclusionVerified && !context.NeedsUserExplanation)
                return AiEscalationDecision.LocalOnly("Verified local logic already resolved the question.");

            if (context.CachedVerifiedFindingAvailable)
                return AiEscalationDecision.LocalOnly("A verified cached finding can be reused without another AI request.");

            if (context.AuthoritativeExternalConclusionVerified && !context.NeedsInterpretation)
                return AiEscalationDecision.LocalOnly("Authoritative research already produced a sufficient verified conclusion.");

            if (!context.LocalEvidenceInsufficient && !context.NeedsInterpretation && !context.NeedsUserExplanation)
                return AiEscalationDecision.LocalOnly("Deterministic Sentinel intelligence is sufficient.");

            if (!context.AuthoritativeResearchAttempted && context.ExternalResearchApplicable)
                return new AiEscalationDecision(false, true, AiModelTier.None, 0,
                    "Use authoritative external research before consuming AI tokens.");

            if (!context.LocalEvidenceAvailable)
                return AiEscalationDecision.LocalOnly("Sentinel will not spend AI tokens to speculate without verified local evidence.");

            AiModelTier tier = context.HighComplexity || context.HighRisk
                ? AiModelTier.Advanced
                : AiModelTier.Economy;

            int inputBudget = tier == AiModelTier.Advanced ? 1800 : 900;
            int outputBudget = context.NeedsUserExplanation ? 500 : 300;

            return new AiEscalationDecision(true, false, tier, inputBudget + outputBudget,
                tier == AiModelTier.Advanced
                    ? "AI escalation is justified for a complex or high-risk unresolved investigation."
                    : "A compact economy-model request is justified after local and authoritative methods were insufficient.");
        }
    }

    public sealed record AiEscalationContext(
        bool LocalEvidenceAvailable,
        bool LocalEvidenceInsufficient,
        bool LocalConclusionVerified,
        bool CachedVerifiedFindingAvailable,
        bool ExternalResearchApplicable,
        bool AuthoritativeResearchAttempted,
        bool AuthoritativeExternalConclusionVerified,
        bool NeedsInterpretation,
        bool NeedsUserExplanation,
        bool HighComplexity,
        bool HighRisk);

    public sealed record AiEscalationDecision(
        bool UseCloudAi,
        bool ResearchFirst,
        AiModelTier ModelTier,
        int MaximumTotalTokens,
        string Reason)
    {
        public static AiEscalationDecision LocalOnly(string reason) =>
            new(false, false, AiModelTier.None, 0, reason);
    }

    public enum AiModelTier
    {
        None,
        Economy,
        Advanced
    }
}
