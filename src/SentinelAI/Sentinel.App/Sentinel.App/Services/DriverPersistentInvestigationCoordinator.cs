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
                plan.Available ? RepairAttemptOutcome.AwaitingApproval : RepairAttemptOutcome.Unavailable,
                now,
                plan.Available
                    ? "Windows Update offered a compatible signed driver and is awaiting approval."
                    : "Windows Update did not provide a verified compatible automatic driver repair."));

            AddOrReplace(attempts, new RepairAttemptRecord(
                "Microsoft Update Catalog",
                plan.Available ? RepairAttemptOutcome.NotApplicable : RepairAttemptOutcome.Unavailable,
                now,
                plan.Available
                    ? "A verified Windows Update package was found, so a separate catalog package was not required."
                    : "No exact automatically installable catalog repair was verified."));

            if (plan.ResearchPerformed)
            {
                AddOrReplace(attempts, new RepairAttemptRecord(
                    "Computer manufacturer support",
                    plan.Available ? RepairAttemptOutcome.AwaitingApproval : RepairAttemptOutcome.Unavailable,
                    now,
                    string.IsNullOrWhiteSpace(plan.Source) ? plan.Summary : $"{plan.Source}: {plan.Summary}"));

                if (!plan.Available && !plan.AutomaticInstallationVerified)
                {
                    AddOrReplace(attempts, new RepairAttemptRecord(
                        "Driver reinstall",
                        RepairAttemptOutcome.NotApplicable,
                        now,
                        "No verified replacement package was found, so repeating the same installed package is not treated as a verified repair."));

                    AddOrReplace(attempts, new RepairAttemptRecord(
                        "Driver rollback",
                        RepairAttemptOutcome.NotApplicable,
                        now,
                        "No verified compatible rollback package was identified for this exact condition."));

                    AddOrReplace(attempts, new RepairAttemptRecord(
                        "BIOS or firmware verification",
                        RepairAttemptOutcome.NotApplicable,
                        now,
                        "The authoritative research result did not identify a verified firmware remediation for this exact condition."));
                }
            }

            bool allRequiredPathsClosed = RequiredRepairPaths.All(path =>
                attempts.Any(attempt =>
                    string.Equals(attempt.RepairPath, path, StringComparison.OrdinalIgnoreCase) &&
                    attempt.Outcome is RepairAttemptOutcome.Succeeded
                        or RepairAttemptOutcome.Failed
                        or RepairAttemptOutcome.Unavailable
                        or RepairAttemptOutcome.NotApplicable
                        or RepairAttemptOutcome.UserDeclined));

            bool noVerifiedRepairRemains =
                plan.ResearchPerformed &&
                !plan.Available &&
                !plan.AutomaticInstallationVerified &&
                allRequiredPathsClosed;

            InvestigationLifecycleState state = plan.Available
                ? InvestigationLifecycleState.RequiresUserApproval
                : noVerifiedRepairRemains
                    ? InvestigationLifecycleState.PersistentNoncritical
                    : plan.UserActionRequired
                        ? InvestigationLifecycleState.RequiresManualRepair
                        : InvestigationLifecycleState.InvestigationIncomplete;

            string evidenceSummary = state == InvestigationLifecycleState.PersistentNoncritical
                ? "Sentinel completed Windows Update and authoritative driver research and did not verify a safe installable repair for this exact condition. No verified remediation path is currently available. Sentinel can keep monitoring it silently and will reopen the investigation if material evidence changes."
                : plan.Summary;

            var record = new PersistentInvestigationRecord(
                existing?.InvestigationId ?? Guid.NewGuid(),
                fingerprint,
                "Driver",
                string.IsNullOrWhiteSpace(deviceName) ? "Windows driver condition" : deviceName.Trim(),
                evidenceSummary,
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
