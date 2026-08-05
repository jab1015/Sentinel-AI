/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Produces low-noise diagnostic records for material Discovery changes.
    /// Unchanged observations do not generate history noise.
    /// </summary>
    public sealed class EventDrivenDiscoveryDiagnosticService
    {
        private DiagnosticState? _lastRecorded;

        public DiagnosticResult Evaluate(
            LiveEventDrivenDiscoveryCoordinator.LiveEventDrivenDecision decision)
        {
            ArgumentNullException.ThrowIfNull(decision);

            if (!decision.MaterialChangeDetected)
                return new DiagnosticResult(false, string.Empty, string.Empty, string.Empty);

            DiagnosticState current = new(decision.Kind, decision.ForceImmediateRecheck, decision.Reason);
            if (_lastRecorded is not null && _lastRecorded == current)
                return new DiagnosticResult(false, string.Empty, string.Empty, string.Empty);

            _lastRecorded = current;

            string title = decision.Kind switch
            {
                DiscoveryChangeDetectionService.ChangeKind.SecurityPostureChanged => "Security posture changed",
                DiscoveryChangeDetectionService.ChangeKind.CriticalEvidenceAppeared => "Critical evidence detected",
                DiscoveryChangeDetectionService.ChangeKind.PersistentConditionChanged => "Known condition changed",
                DiscoveryChangeDetectionService.ChangeKind.EvidenceFingerprintChanged => "Discovery evidence changed",
                DiscoveryChangeDetectionService.ChangeKind.AttentionStateChanged => "Attention state changed",
                DiscoveryChangeDetectionService.ChangeKind.OperatingContextChanged => "Discovery operating context changed",
                _ => "Discovery state changed"
            };

            string summary = decision.ForceImmediateRecheck
                ? $"{decision.Reason} Sentinel scheduled an immediate confirmation recheck."
                : $"{decision.Reason} Sentinel recalculated monitoring without forcing an urgent refresh.";

            string technical =
                $"Change kind: {decision.Kind}; Material change: {decision.MaterialChangeDetected}; " +
                $"Immediate recheck: {decision.ForceImmediateRecheck}; Monitoring enabled: true; Reason: {decision.Reason}";

            return new DiagnosticResult(true, title, summary, technical);
        }

        private sealed record DiagnosticState(
            DiscoveryChangeDetectionService.ChangeKind Kind,
            bool ForceImmediateRecheck,
            string Reason);

        public sealed record DiagnosticResult(
            bool ShouldRecord,
            string Title,
            string Summary,
            string TechnicalDetail);
    }
}
