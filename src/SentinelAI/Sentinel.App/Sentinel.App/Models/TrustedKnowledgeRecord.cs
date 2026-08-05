/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;

namespace Sentinel.App.Models
{
    public sealed record TrustedKnowledgeRecord(
        Guid KnowledgeId,
        Guid SourceInvestigationId,
        string KnowledgeKey,
        string FindingType,
        string RootCause,
        string Conclusion,
        int ConfidencePercent,
        string TrustLevel,
        string RiskClassification,
        IReadOnlyList<RepairAttemptRecord> VerifiedRepairHistory,
        InvestigationInvalidationState EvidenceState,
        DateTimeOffset CreatedUtc,
        DateTimeOffset LastVerifiedUtc,
        DateTimeOffset? ExpiresUtc,
        bool IsReusable,
        string InvalidationReason)
    {
        public bool IsExpired(DateTimeOffset now) => ExpiresUtc.HasValue && ExpiresUtc.Value <= now;
    }
}
