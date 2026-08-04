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
    /// <summary>
    /// Connects short-lived remediation approval to verified quarantine and restore
    /// operations. The approved action and target must still match the current
    /// investigation immediately before execution.
    /// </summary>
    public sealed class ApprovedQuarantineCoordinator
    {
        private readonly ApprovedRemediationExecutor _approvedExecutor;
        private readonly QuarantineService _quarantineService;
        private readonly QuarantineCatalogService _catalogService;

        public ApprovedQuarantineCoordinator(
            ApprovedRemediationExecutor? approvedExecutor = null,
            QuarantineService? quarantineService = null,
            QuarantineCatalogService? catalogService = null)
        {
            _approvedExecutor = approvedExecutor ?? new ApprovedRemediationExecutor();
            _quarantineService = quarantineService ?? new QuarantineService();
            _catalogService = catalogService ?? new QuarantineCatalogService();
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

            if (string.Equals(request.Action, "quarantine-file", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteQuarantineAsync(
                    currentSnapshot,
                    request,
                    validation,
                    cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(request.Action, "restore-quarantined-file", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteRestoreAsync(
                    currentSnapshot,
                    request,
                    validation,
                    cancellationToken).ConfigureAwait(false);
            }

            return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                "This approval is not for quarantine or restore. Sentinel made no system change.");
        }

        private async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteQuarantineAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            CancellationToken cancellationToken)
        {
            QuarantineService.QuarantineResult? quarantineResult = null;

            return await _approvedExecutor.ExecuteAsync(
                currentSnapshot,
                request,
                validation,
                executeAsync: async () =>
                {
                    quarantineResult = await _quarantineService.QuarantineAsync(
                        request.Target,
                        hasVerifiedEvidence: true,
                        isWindowsProtectedComponent: false,
                        userApproved: true,
                        cancellationToken).ConfigureAwait(false);

                    if (!quarantineResult.Succeeded ||
                        !quarantineResult.Verified ||
                        quarantineResult.Record is null)
                    {
                        throw new InvalidOperationException(quarantineResult.Message);
                    }

                    await _catalogService.AddAsync(
                        quarantineResult.Record,
                        cancellationToken).ConfigureAwait(false);
                },
                verifyAsync: () => Task.FromResult(
                    quarantineResult?.Succeeded == true &&
                    quarantineResult.Verified &&
                    quarantineResult.Record is not null))
                .ConfigureAwait(false);
        }

        private async Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteRestoreAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            CancellationToken cancellationToken)
        {
            var entries = await _catalogService.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
            QuarantineCatalogService.QuarantineCatalogEntry? entry = entries.FirstOrDefault(item =>
                string.Equals(item.OriginalPath, request.Target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.QuarantinePath, request.Target, StringComparison.OrdinalIgnoreCase));

            if (entry is null || !entry.IsPresent)
            {
                return ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                    "Sentinel could not verify the approved quarantined file. No restore was attempted.");
            }

            QuarantineService.QuarantineRecord record = _catalogService.ToRecord(entry);
            QuarantineService.QuarantineResult? restoreResult = null;

            ApprovedRemediationExecutor.ApprovedRemediationResult result =
                await _approvedExecutor.ExecuteAsync(
                    currentSnapshot,
                    request,
                    validation,
                    executeAsync: async () =>
                    {
                        restoreResult = await _quarantineService.RestoreAsync(
                            record,
                            userApproved: true,
                            cancellationToken).ConfigureAwait(false);

                        if (!restoreResult.Succeeded || !restoreResult.Verified)
                        {
                            throw new InvalidOperationException(restoreResult.Message);
                        }
                    },
                    verifyAsync: () => Task.FromResult(
                        restoreResult?.Succeeded == true && restoreResult.Verified))
                    .ConfigureAwait(false);

            if (result.Verified)
            {
                await _catalogService.RemoveAsync(
                    record.QuarantinePath,
                    cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
    }
}
