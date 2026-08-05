/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Resolves current Discovery evidence against durable investigation memory.
    /// This coordinator controls notification presentation only; monitoring always continues.
    /// </summary>
    public sealed class LivePersistentExceptionCoordinator
    {
        private readonly PersistentInvestigationMemoryService _memoryService;
        private readonly PersistentExceptionPresentationService _presentationService;

        public LivePersistentExceptionCoordinator(
            PersistentInvestigationMemoryService? memoryService = null,
            PersistentExceptionPresentationService? presentationService = null)
        {
            _memoryService = memoryService ?? new PersistentInvestigationMemoryService();
            _presentationService = presentationService ?? new PersistentExceptionPresentationService();
        }

        public async Task<LivePersistentExceptionResult> EvaluateAsync(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            bool isDriverFinding =
                snapshot.InvestigationRequiresAttention &&
                (snapshot.InvestigationReasonCode?.StartsWith("driver:", StringComparison.OrdinalIgnoreCase) == true ||
                 snapshot.GuidanceTitle?.Contains("driver", StringComparison.OrdinalIgnoreCase) == true);

            if (!isDriverFinding)
                return LivePersistentExceptionResult.None;

            var records = await _memoryService.ReadAllAsync().ConfigureAwait(false);
            PersistentInvestigationRecord? record = records
                .Where(item => item.FindingType.Equals("Driver", StringComparison.OrdinalIgnoreCase))
                .Where(item => CurrentEvidenceMatches(snapshot, item))
                .OrderByDescending(item => item.LastVerifiedUtc)
                .FirstOrDefault();

            if (record is null)
                return LivePersistentExceptionResult.None;

            PersistentExceptionPresentationService.PresentationDecision decision =
                _presentationService.Evaluate(record);

            return new LivePersistentExceptionResult(record, decision, true);
        }

        public Task<PersistentInvestigationMemoryService.SuppressionDecision> SetSilentMonitoringAsync(
            PersistentInvestigationRecord record,
            bool suppress)
        {
            ArgumentNullException.ThrowIfNull(record);
            return _memoryService.SetSilentMonitoringAsync(
                record.Fingerprint,
                suppress,
                suppress
                    ? "User selected silent monitoring after Sentinel verified exhaustive remediation and noncritical risk."
                    : string.Empty);
        }

        private static bool CurrentEvidenceMatches(SystemSnapshot snapshot, PersistentInvestigationRecord record)
        {
            string rootCause = record.RootCause?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rootCause))
                return false;

            return Contains(snapshot.GuidanceWhatHappened, rootCause) ||
                   Contains(snapshot.InvestigationSummary, rootCause) ||
                   Contains(snapshot.GuidanceEvidence, rootCause) ||
                   Contains(snapshot.InvestigationReasonCode, NormalizeDevice(rootCause));
        }

        private static bool Contains(string? source, string value) =>
            !string.IsNullOrWhiteSpace(source) &&
            source.Contains(value, StringComparison.OrdinalIgnoreCase);

        private static string NormalizeDevice(string value) =>
            value.Trim().ToLowerInvariant();

        public sealed record LivePersistentExceptionResult(
            PersistentInvestigationRecord? Record,
            PersistentExceptionPresentationService.PresentationDecision? Decision,
            bool HasMatchingMemory)
        {
            public static LivePersistentExceptionResult None { get; } = new(null, null, false);

            public bool SuppressNotification => Decision?.SuppressNotification == true;
            public bool ShowKnownCondition => Decision?.ShowKnownCondition == true;
            public bool CanToggleNotifications =>
                Record?.IsEligibleForSilentMonitoring == true &&
                Decision is not null;
        }
    }
}
