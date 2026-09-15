using System;

namespace Sentinel.App.Models
{
    public sealed class SystemSnapshot
    {
    }
}

namespace Sentinel.App.Services
{
    public sealed class AskSentinelResponseOrchestrator
    {
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
