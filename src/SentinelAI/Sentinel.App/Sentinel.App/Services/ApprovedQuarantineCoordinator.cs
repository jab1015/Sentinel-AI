/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    public sealed class ApprovedQuarantineCoordinator
    {
        private static readonly SemaphoreSlim QuarantineGate = new(1, 1);
        private readonly ApprovedRemediationExecutor _approvedExecutor;
        private readonly QuarantineService _quarantineService;
        private readonly QuarantineCatalogService _catalogService;
        private readonly MaintenanceOutcomeRecorder _outcomeRecorder;

        public ApprovedQuarantineCoordinator(
            ApprovedRemediationExecutor? approvedExecutor = null,
            QuarantineService? quarantineService = null,
            QuarantineCatalogService? catalogService = null,
            MaintenanceOutcomeRecorder? outcomeRecorder = null)
        {
            _approvedExecutor = approvedExecutor ?? new ApprovedRemediationExecutor();
            _quarantineService = quarantineService ?? new QuarantineService();
            _catalogService = catalogService ?? new QuarantineCatalogService();
            _outcomeRecorder = outcomeRecorder ?? new MaintenanceOutcomeRecorder();
        }

        public async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(currentSnapshot);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(validation);

            await QuarantineGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
            if (string.Equals(request.Action, "quarantine-file", StringComparison.OrdinalIgnoreCase))
                return await ExecuteQuarantineAsync(currentSnapshot, request, validation, cancellationToken).ConfigureAwait(false);

            if (string.Equals(request.Action, "restore-quarantined-file", StringComparison.OrdinalIgnoreCase))
                return await ExecuteRestoreAsync(currentSnapshot, request, validation, cancellationToken).ConfigureAwait(false);

            if (string.Equals(request.Action, "delete-quarantined-file", StringComparison.OrdinalIgnoreCase))
                return await ExecuteDeleteAsync(currentSnapshot, request, validation, cancellationToken).ConfigureAwait(false);

            return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                "This approval is not for a supported quarantine action. Sentinel made no system change.");
            }
            finally
            {
                QuarantineGate.Release();
            }
        }

        private async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteQuarantineAsync(
            SystemSnapshot snapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            CancellationToken token)
        {
            QuarantineService.QuarantineResult? operation = null;
            ApprovedRemediationExecutor.ApprovedRemediationResult result = await _approvedExecutor.ExecuteAsync(
                snapshot,
                request,
                validation,
                async () =>
                {
                    operation = await _quarantineService.QuarantineAsync(request.Target, true, false, true, token).ConfigureAwait(false);
                    _outcomeRecorder.Record(operation, "Quarantine file");
                    if (!operation.Succeeded || !operation.Verified || operation.Record is null)
                        throw new InvalidOperationException(operation.Message);

                    await _catalogService.AddAsync(
                        operation.Record,
                        request.ReasonCode,
                        request.EvidenceConfidencePercent,
                        token).ConfigureAwait(false);
                },
                () => Task.FromResult(operation?.Succeeded == true && operation.Verified && operation.Record is not null))
                .ConfigureAwait(false);

            return result;
        }

        private async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteRestoreAsync(
            SystemSnapshot snapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            CancellationToken token)
        {
            QuarantineCatalogService.QuarantineCatalogEntry? entry = await FindEntryAsync(request.Target, token).ConfigureAwait(false);
            if (entry is null || !entry.IsPresent)
                return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                    "Sentinel could not verify the approved quarantined file. No restore was attempted.");

            QuarantineService.QuarantineRecord record = _catalogService.ToRecord(entry);
            QuarantineService.QuarantineResult? operation = null;

            ApprovedRemediationExecutor.ApprovedRemediationResult result = await _approvedExecutor.ExecuteAsync(
                snapshot,
                request,
                validation,
                async () =>
                {
                    operation = await _quarantineService.RestoreAsync(record, true, token).ConfigureAwait(false);
                    _outcomeRecorder.Record(operation, "Restore quarantined file");
                    if (!operation.Succeeded || !operation.Verified)
                        throw new InvalidOperationException(operation.Message);
                },
                () => Task.FromResult(operation?.Succeeded == true && operation.Verified))
                .ConfigureAwait(false);

            if (result.Verified)
                await _catalogService.RemoveAsync(record.QuarantinePath, token).ConfigureAwait(false);

            return result;
        }

        private async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteDeleteAsync(
            SystemSnapshot snapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            CancellationToken token)
        {
            QuarantineCatalogService.QuarantineCatalogEntry? entry = await FindEntryAsync(request.Target, token).ConfigureAwait(false);
            if (entry is null || !entry.IsPresent)
                return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                    "Sentinel could not verify the approved quarantined file. Nothing was deleted.");

            QuarantineService.QuarantineRecord record = _catalogService.ToRecord(entry);
            QuarantineService.QuarantineResult? operation = null;

            ApprovedRemediationExecutor.ApprovedRemediationResult result = await _approvedExecutor.ExecuteAsync(
                snapshot,
                request,
                validation,
                async () =>
                {
                    operation = await _quarantineService.DeletePermanentlyAsync(record, true, token).ConfigureAwait(false);
                    _outcomeRecorder.Record(operation, "Delete quarantined file permanently");
                    if (!operation.Succeeded || !operation.Verified)
                        throw new InvalidOperationException(operation.Message);
                },
                () => Task.FromResult(operation?.Succeeded == true && operation.Verified))
                .ConfigureAwait(false);

            if (result.Verified)
                await _catalogService.RemoveAsync(record.QuarantinePath, token).ConfigureAwait(false);

            return result;
        }

        private async Task<QuarantineCatalogService.QuarantineCatalogEntry?> FindEntryAsync(string target, CancellationToken token)
        {
            var entries = await _catalogService.ReconcileAsync(token).ConfigureAwait(false);
            return entries.FirstOrDefault(item =>
                string.Equals(item.OriginalPath, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.QuarantinePath, target, StringComparison.OrdinalIgnoreCase));
        }
    }
}
