/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Maintains the previous live Discovery state and converts snapshot changes into
    /// event-driven recheck decisions. This coordinator is UI-independent so it can
    /// be validated before the final WinUI timer hook is added.
    /// </summary>
    public sealed class LiveEventDrivenDiscoveryCoordinator
    {
        private readonly DiscoveryChangeDetectionService _changeDetectionService;
        private LiveState? _previous;

        public LiveEventDrivenDiscoveryCoordinator(DiscoveryChangeDetectionService? changeDetectionService = null)
        {
            _changeDetectionService = changeDetectionService ?? new DiscoveryChangeDetectionService();
        }

        public LiveEventDrivenDecision Evaluate(
            SystemSnapshot snapshot,
            bool persistentNotificationSuppressed,
            bool persistentConditionMateriallyChanged = false,
            bool onBattery = false,
            bool applicationIsIdle = false)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            LiveState current = Capture(snapshot, persistentNotificationSuppressed, onBattery, applicationIsIdle);
            if (_previous is null)
            {
                _previous = current;
                return new LiveEventDrivenDecision(
                    false,
                    DiscoveryChangeDetectionService.ChangeKind.None,
                    false,
                    "Initial live Discovery state captured.");
            }

            LiveState previous = _previous;
            _previous = current;

            bool fingerprintChanged = !string.Equals(
                current.EvidenceFingerprint,
                previous.EvidenceFingerprint,
                StringComparison.OrdinalIgnoreCase);

            bool securityPostureChanged =
                current.DefenderEnabled != previous.DefenderEnabled ||
                current.FirewallEnabled != previous.FirewallEnabled;

            DiscoveryChangeDetectionService.ChangeDetectionInput input = new(
                EvidenceFingerprintChanged: fingerprintChanged,
                SecurityPostureChanged: securityPostureChanged,
                CriticalEvidencePresent: current.CriticalEvidencePresent,
                PreviousCriticalEvidencePresent: previous.CriticalEvidencePresent,
                AttentionRequired: current.AttentionRequired,
                PreviousAttentionRequired: previous.AttentionRequired,
                PersistentConditionWasSuppressed: previous.PersistentNotificationSuppressed,
                PersistentConditionMateriallyChanged: persistentConditionMateriallyChanged,
                PowerSourceChanged: current.OnBattery != previous.OnBattery,
                IdleStateChanged: current.ApplicationIsIdle != previous.ApplicationIsIdle);

            DiscoveryChangeDetectionService.ChangeDetectionResult change =
                _changeDetectionService.Evaluate(input);

            return new LiveEventDrivenDecision(
                change.MaterialChangeDetected,
                change.Kind,
                change.ForceImmediateRecheck,
                change.Reason);
        }

        public void Reset() => _previous = null;

        private static LiveState Capture(
            SystemSnapshot snapshot,
            bool persistentNotificationSuppressed,
            bool onBattery,
            bool applicationIsIdle)
        {
            string fingerprint = BuildEvidenceFingerprint(snapshot);
            bool critical =
                !snapshot.DefenderEnabled ||
                !snapshot.FirewallEnabled ||
                snapshot.InvestigationShouldEscalate ||
                Contains(snapshot.GuidanceSeverity, "critical") ||
                Contains(snapshot.RiskLevel, "critical");

            bool attention =
                snapshot.InvestigationRequiresAttention ||
                snapshot.AutonomousProtectionRequiresUserApproval ||
                string.Equals(snapshot.MemoryPressureLevel, "High", StringComparison.OrdinalIgnoreCase);

            return new LiveState(
                fingerprint,
                snapshot.DefenderEnabled,
                snapshot.FirewallEnabled,
                critical,
                attention,
                persistentNotificationSuppressed,
                onBattery,
                applicationIsIdle);
        }

        private static string BuildEvidenceFingerprint(SystemSnapshot snapshot)
        {
            string investigation = Normalize(snapshot.InvestigationReasonCode);
            string guidance = Normalize(snapshot.GuidanceTitle);
            string eventSource = Normalize(snapshot.LatestEventSource);
            string driverOrTarget = Normalize(snapshot.RemediationTarget);
            string network = Normalize(snapshot.PrimaryFlaggedConnectionRemoteEndpoint);

            return string.Join("|",
                investigation,
                guidance,
                eventSource,
                driverOrTarget,
                network,
                snapshot.FlaggedProcessCount,
                snapshot.FlaggedServiceCount,
                snapshot.FlaggedConnectionCount,
                snapshot.CriticalEventCount,
                snapshot.ErrorEventCount);
        }

        private static string Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "none" : value.Trim().ToLowerInvariant();

        private static bool Contains(string? value, string term) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(term, StringComparison.OrdinalIgnoreCase);

        private sealed record LiveState(
            string EvidenceFingerprint,
            bool DefenderEnabled,
            bool FirewallEnabled,
            bool CriticalEvidencePresent,
            bool AttentionRequired,
            bool PersistentNotificationSuppressed,
            bool OnBattery,
            bool ApplicationIsIdle);

        public sealed record LiveEventDrivenDecision(
            bool MaterialChangeDetected,
            DiscoveryChangeDetectionService.ChangeKind Kind,
            bool ForceImmediateRecheck,
            string Reason);
    }
}
