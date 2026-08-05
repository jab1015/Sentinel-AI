/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;

namespace Sentinel.App.Models
{
    public enum InvestigationLifecycleState
    {
        Discovered,
        EvidenceCollected,
        Correlated,
        Investigating,
        RepairAttempted,
        Resolved,
        RequiresUserApproval,
        RequiresManualRepair,
        PersistentNoncritical,
        Critical,
        InvestigationIncomplete
    }

    public enum RepairAttemptOutcome
    {
        Succeeded,
        Failed,
        Unavailable,
        NotApplicable,
        UserDeclined,
        AwaitingApproval
    }

    public sealed record RepairAttemptRecord(
        string RepairPath,
        RepairAttemptOutcome Outcome,
        DateTimeOffset TimestampUtc,
        string Summary);

    public sealed record InvestigationInvalidationState(
        string DeviceInstanceId,
        string HardwareId,
        string ErrorCode,
        string DriverVersion,
        string WindowsBuild,
        string BiosVersion,
        string Manufacturer,
        string Model,
        string Severity,
        string VerifiedRepairSignature)
    {
        public static InvestigationInvalidationState Empty { get; } = new(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
    }

    public sealed record PersistentInvestigationRecord(
        Guid InvestigationId,
        string Fingerprint,
        string FindingType,
        string RootCause,
        string EvidenceSummary,
        int ConfidencePercent,
        string TrustLevel,
        string RiskClassification,
        InvestigationLifecycleState State,
        IReadOnlyList<RepairAttemptRecord> RepairAttempts,
        DateTimeOffset FirstDetectedUtc,
        DateTimeOffset LastVerifiedUtc,
        InvestigationInvalidationState InvalidationState,
        bool NotificationsSuppressed,
        DateTimeOffset? SuppressedAtUtc,
        string SuppressionReason)
    {
        public bool IsCritical => State == InvestigationLifecycleState.Critical;

        public bool HasExhaustedRepairLedger =>
            RepairAttempts.Count > 0 &&
            Array.TrueForAll(
                System.Linq.Enumerable.ToArray(RepairAttempts),
                attempt => attempt.Outcome != RepairAttemptOutcome.AwaitingApproval);

        public bool IsEligibleForSilentMonitoring =>
            State == InvestigationLifecycleState.PersistentNoncritical &&
            !IsCritical &&
            HasExhaustedRepairLedger;
    }
}
