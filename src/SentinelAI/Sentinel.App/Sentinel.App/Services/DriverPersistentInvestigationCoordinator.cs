/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts verified driver-repair outcomes into durable investigation memory.
    /// A driver condition is not eligible for silent monitoring while an official
    /// user-action repair path remains open or any required repair path is unverified.
    /// </summary>
    public sealed class DriverPersistentInvestigationCoordinator
    {
        private static readonly string[] RequiredRepairPaths =
        {
            "Windows Update",
            "Microsoft Update Catalog",
            "Computer manufacturer support",
            "Driver reinstall",
            "Driver rollback",
            "BIOS or firmware verification"
        };

        private readonly PersistentInvestigationMemoryService _memoryService;

        public DriverPersistentInvestigationCoordinator(PersistentInvestigationMemoryService? memoryService = null)
        {
            _memoryService = memoryService ?? new PersistentInvestigationMemoryService();
        }

        public async Task<PersistentInvestigationRecord> RecordResearchOutcomeAsync(
            string deviceName,
            string errorCode,
            DriverAutomaticRepairCoordinator.DriverRepairPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var invalidation = new InvestigationInvalidationState(
                DeviceInstanceId: deviceName?.Trim() ?? string.Empty,
                HardwareId: string.Empty,
                ErrorCode: errorCode?.Trim() ?? string.Empty,
                DriverVersion: string.Empty,
                WindowsBuild: Environment.OSVersion.Version.ToString(),
                BiosVersion: string.Empty,
                Manufacturer: string.Empty,
                Model: string.Empty,
                Severity: "Attention",
                VerifiedRepairSignature: BuildRepairSignature(plan));

            string fingerprint = PersistentInvestigationMemoryService.CreateFingerprint("driver", invalidation);
            PersistentInvestigationRecord? existing =
                await _memoryService.FindReusableAsync(fingerprint, invalidation).ConfigureAwait(false);

            List<RepairAttemptRecord> attempts = existing?.RepairAttempts.ToList()
                ?? new List<RepairAttemptRecord>();

            AddOrReplace(attempts, new RepairAttemptRecord(
                "Windows Update",
                plan.Available
                    ? RepairAttemptOutcome.AwaitingApproval
                    : RepairAttemptOutcome.Unavailable,
                now,
                plan.Available
                    ? "Windows Update offered a compatible signed driver and is awaiting approval."
                    : "Windows Update did not provide a verified compatible automatic driver repair."));

            AddOrReplace(attempts, new RepairAttemptRecord(
                "Microsoft Update Catalog",
                plan.Available
                    ? RepairAttemptOutcome.NotApplicable
                    : RepairAttemptOutcome.Unavailable,
                now,
                plan.Available
                    ? "A verified Windows Update package was found, so a separate catalog package was not required."
                    : "No exact automatically installable catalog repair was verified."));

            if (plan.ResearchPerformed)
            {
                AddOrReplace(attempts, new RepairAttemptRecord(
                    "Computer manufacturer support",
                    plan.UserActionRequired
                        ? RepairAttemptOutcome.AwaitingApproval
                        : RepairAttemptOutcome.Unavailable,
                    now,
                    string.IsNullOrWhiteSpace(plan.Source)
                        ? plan.Summary
                        : $"{plan.Source}: {plan.Summary}"));
            }

            bool allRequiredPathsClosed = RequiredRepairPaths.All(path =>
                attempts.Any(attempt =>
                    string.Equals(attempt.RepairPath, path, StringComparison.OrdinalIgnoreCase) &&
                    attempt.Outcome is RepairAttemptOutcome.Succeeded
                        or RepairAttemptOutcome.Failed
                        or RepairAttemptOutcome.Unavailable
                        or RepairAttemptOutcome.NotApplicable
                        or RepairAttemptOutcome.UserDeclined));

            InvestigationLifecycleState state = plan.Available
                ? InvestigationLifecycleState.RequiresUserApproval
                : plan.UserActionRequired
                    ? InvestigationLifecycleState.RequiresManualRepair
                    : allRequiredPathsClosed
                        ? InvestigationLifecycleState.PersistentNoncritical
                        : InvestigationLifecycleState.InvestigationIncomplete;

            var record = new PersistentInvestigationRecord(
                existing?.InvestigationId ?? Guid.NewGuid(),
                fingerprint,
                "Driver",
                string.IsNullOrWhiteSpace(deviceName) ? "Windows driver condition" : deviceName.Trim(),
                plan.Summary,
                plan.ConfidencePercent,
                string.IsNullOrWhiteSpace(plan.TrustStatement) ? "Verified local evidence" : plan.TrustStatement,
                state == InvestigationLifecycleState.PersistentNoncritical ? "Persistent noncritical" : "Attention",
                state,
                attempts,
                existing?.FirstDetectedUtc ?? now,
                now,
                invalidation,
                state == InvestigationLifecycleState.PersistentNoncritical && existing?.NotificationsSuppressed == true,
                state == InvestigationLifecycleState.PersistentNoncritical ? existing?.SuppressedAtUtc : null,
                state == InvestigationLifecycleState.PersistentNoncritical ? existing?.SuppressionReason ?? string.Empty : string.Empty);

            await _memoryService.UpsertAsync(record).ConfigureAwait(false);
            return record;
        }

        public async Task<PersistentInvestigationMemoryService.SuppressionDecision> SetSilentMonitoringAsync(
            PersistentInvestigationRecord record,
            bool suppress,
            string reason)
        {
            ArgumentNullException.ThrowIfNull(record);
            return await _memoryService
                .SetSilentMonitoringAsync(record.Fingerprint, suppress, reason)
                .ConfigureAwait(false);
        }

        private static string BuildRepairSignature(DriverAutomaticRepairCoordinator.DriverRepairPlan plan) =>
            string.Join("|",
                plan.Available,
                plan.AutomaticInstallationVerified,
                plan.ResearchPerformed,
                plan.UserActionRequired,
                plan.Source?.Trim() ?? string.Empty,
                plan.PackageTitle?.Trim() ?? string.Empty,
                plan.ConfidencePercent);

        private static void AddOrReplace(List<RepairAttemptRecord> attempts, RepairAttemptRecord replacement)
        {
            int index = attempts.FindIndex(attempt =>
                string.Equals(attempt.RepairPath, replacement.RepairPath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                attempts[index] = replacement;
            else
                attempts.Add(replacement);
        }
    }
}
