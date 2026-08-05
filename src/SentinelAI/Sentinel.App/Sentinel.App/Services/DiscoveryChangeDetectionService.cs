/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Determines whether newly observed Discovery evidence represents a material
    /// change that should force immediate re-evaluation instead of waiting for the
    /// ordinary adaptive cadence. This is deliberately evidence-oriented: a mere
    /// polling cycle is not a change.
    /// </summary>
    public sealed class DiscoveryChangeDetectionService
    {
        public ChangeDetectionResult Evaluate(ChangeDetectionInput input)
        {
            ArgumentNullException.ThrowIfNull(input);

            if (input.CriticalEvidencePresent && !input.PreviousCriticalEvidencePresent)
            {
                return Changed(
                    ChangeKind.CriticalEvidenceAppeared,
                    "New critical evidence appeared and requires immediate Discovery re-evaluation.",
                    forceImmediateRecheck: true);
            }

            if (input.SecurityPostureChanged)
            {
                return Changed(
                    ChangeKind.SecurityPostureChanged,
                    "Security posture changed and must be re-evaluated immediately.",
                    forceImmediateRecheck: true);
            }

            if (input.EvidenceFingerprintChanged)
            {
                return Changed(
                    ChangeKind.EvidenceFingerprintChanged,
                    "The evidence fingerprint changed, invalidating any unchanged-evidence assumption.",
                    forceImmediateRecheck: true);
            }

            if (input.PersistentConditionWasSuppressed && input.PersistentConditionMateriallyChanged)
            {
                return Changed(
                    ChangeKind.PersistentConditionChanged,
                    "A silently monitored persistent condition materially changed and must be reopened.",
                    forceImmediateRecheck: true);
            }

            if (input.AttentionRequired != input.PreviousAttentionRequired)
            {
                return Changed(
                    ChangeKind.AttentionStateChanged,
                    "The Discovery attention state changed and scheduling should be recalculated.",
                    forceImmediateRecheck: input.AttentionRequired);
            }

            if (input.PowerSourceChanged || input.IdleStateChanged)
            {
                return Changed(
                    ChangeKind.OperatingContextChanged,
                    "System operating conditions changed and adaptive Discovery cadence should be recalculated.",
                    forceImmediateRecheck: false);
            }

            return new ChangeDetectionResult(
                false,
                ChangeKind.None,
                false,
                "No material Discovery evidence change was detected.");
        }

        private static ChangeDetectionResult Changed(
            ChangeKind kind,
            string reason,
            bool forceImmediateRecheck) =>
            new(true, kind, forceImmediateRecheck, reason);

        public sealed record ChangeDetectionInput(
            bool EvidenceFingerprintChanged,
            bool SecurityPostureChanged,
            bool CriticalEvidencePresent,
            bool PreviousCriticalEvidencePresent,
            bool AttentionRequired,
            bool PreviousAttentionRequired,
            bool PersistentConditionWasSuppressed,
            bool PersistentConditionMateriallyChanged,
            bool PowerSourceChanged,
            bool IdleStateChanged);

        public sealed record ChangeDetectionResult(
            bool MaterialChangeDetected,
            ChangeKind Kind,
            bool ForceImmediateRecheck,
            string Reason);

        public enum ChangeKind
        {
            None,
            CriticalEvidenceAppeared,
            SecurityPostureChanged,
            EvidenceFingerprintChanged,
            PersistentConditionChanged,
            AttentionStateChanged,
            OperatingContextChanged
        }
    }
}
