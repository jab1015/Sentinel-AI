/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// User-mode facade for protected quarantine operations. The desktop process never
    /// receives a filesystem path to the protected payload and never performs restore
    /// or delete from caller-supplied catalog paths. All mutation is delegated to the
    /// allowlisted elevated broker, which owns the ProgramData quarantine boundary.
    /// </summary>
    public sealed class QuarantineService
    {
        private readonly RemediationPolicy _policy;
        private readonly PrivilegedBrokerClient _broker = new();

        public QuarantineService(RemediationPolicy? policy = null)
        {
            _policy = policy ?? new RemediationPolicy();
        }

        public async Task<QuarantineResult> QuarantineAsync(
            string sourcePath,
            bool hasVerifiedEvidence,
            bool isWindowsProtectedComponent,
            bool userApproved,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return Failed("Sentinel could not verify the file to quarantine.");

            var decision = _policy.Evaluate(new RemediationPolicy.RemediationRequest(
                RemediationPolicy.RemediationAction.QuarantineFile,
                RemediationPolicy.RemediationRisk.Moderate,
                hasVerifiedEvidence,
                isWindowsProtectedComponent,
                RequiresElevation: true,
                CanRequestElevation: _broker.IsBrokerPresent));

            if (!decision.Allowed) return Failed(decision.Explanation);
            if (decision.RequiresUserApproval && !userApproved)
                return new QuarantineResult(false, true, false, null, null, decision.Explanation);

            string originalFullPath;
            try { originalFullPath = Path.GetFullPath(sourcePath); }
            catch { return Failed("Sentinel could not canonicalize the file path safely."); }

            string itemId = Guid.NewGuid().ToString("N");
            BrokerInvocationResult result = await _broker.QuarantineFileAsync(originalFullPath, itemId, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
                return Failed($"Sentinel did not report quarantine success. {result.Message}", attempted: result.Code != "ElevationDenied");

            BrokerQuarantineRecord? protectedRecord = await _broker.ReadProtectedRecordAsync(itemId, cancellationToken).ConfigureAwait(false);
            bool protectedPathValid = QuarantineRecordPathPolicy.TryCanonicalize(protectedRecord?.OriginalPath, out string protectedOriginalFullPath);
            if (protectedRecord is null ||
                !protectedPathValid ||
                !protectedRecord.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase) ||
                !protectedOriginalFullPath.Equals(originalFullPath, StringComparison.OrdinalIgnoreCase) ||
                !protectedRecord.Sha256.Equals(result.Sha256, StringComparison.OrdinalIgnoreCase) ||
                File.Exists(originalFullPath))
            {
                return Failed("The privileged broker returned success, but Sentinel could not independently verify the protected quarantine record and removal from the original location.", attempted: true);
            }

            QuarantineRecord record = new(
                protectedRecord.OriginalPath,
                ProtectedReference(itemId),
                protectedRecord.Sha256,
                protectedRecord.QuarantinedAtUtc)
            {
                ItemId = itemId
            };

            return new QuarantineResult(true, false, true, record, record.Sha256,
                "Sentinel moved the approved file into the protected quarantine store and verified the broker-owned record and removal from the original location.",
                Attempted: true);
        }

        public async Task<QuarantineResult> RestoreAsync(
            QuarantineRecord record,
            bool userApproved,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            BrokerQuarantineRecord? protectedRecord = await GetMatchingProtectedRecordAsync(record, cancellationToken).ConfigureAwait(false);

            var decision = _policy.Evaluate(new RemediationPolicy.RemediationRequest(
                RemediationPolicy.RemediationAction.RestoreQuarantinedFile,
                RemediationPolicy.RemediationRisk.Moderate,
                HasVerifiedEvidence: protectedRecord is not null,
                IsWindowsProtectedComponent: false,
                RequiresElevation: true,
                CanRequestElevation: _broker.IsBrokerPresent));

            if (!decision.Allowed) return Failed(decision.Explanation);
            if (decision.RequiresUserApproval && !userApproved)
                return new QuarantineResult(false, true, false, record, record.Sha256, decision.Explanation);
            if (protectedRecord is null)
                return Failed("The protected quarantine record could not be verified. Sentinel did not restore anything.");

            BrokerInvocationResult result = await _broker.RestoreFileAsync(record.ItemId, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
                return Failed($"Sentinel did not verify restore success. {result.Message}", attempted: result.Code != "ElevationDenied");

            bool restored = File.Exists(protectedRecord.OriginalPath);
            string? restoredHash = restored
                ? await PrivilegedBrokerClient.ComputeSha256Async(protectedRecord.OriginalPath, cancellationToken).ConfigureAwait(false)
                : null;
            BrokerQuarantineRecord? remaining = await _broker.ReadProtectedRecordAsync(record.ItemId, cancellationToken).ConfigureAwait(false);
            bool verified = restored && remaining is null &&
                            string.Equals(restoredHash, protectedRecord.Sha256, StringComparison.OrdinalIgnoreCase);

            return verified
                ? new QuarantineResult(true, false, true, record, protectedRecord.Sha256,
                    "Sentinel restored the approved file, verified its hash at the original location, and verified that the protected quarantine record was removed.", true)
                : Failed("The broker reported restore completion, but Sentinel could not verify the restored file identity and record removal.", attempted: true);
        }

        public async Task<QuarantineResult> DeletePermanentlyAsync(
            QuarantineRecord record,
            bool userApproved,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            BrokerQuarantineRecord? protectedRecord = await GetMatchingProtectedRecordAsync(record, cancellationToken).ConfigureAwait(false);

            var decision = _policy.Evaluate(new RemediationPolicy.RemediationRequest(
                RemediationPolicy.RemediationAction.DeleteQuarantinedFile,
                RemediationPolicy.RemediationRisk.Moderate,
                HasVerifiedEvidence: protectedRecord is not null,
                IsWindowsProtectedComponent: false,
                RequiresElevation: true,
                CanRequestElevation: _broker.IsBrokerPresent));

            if (!decision.Allowed) return Failed(decision.Explanation);
            if (decision.RequiresUserApproval && !userApproved)
                return new QuarantineResult(false, true, false, record, record.Sha256, decision.Explanation);
            if (protectedRecord is null)
                return Failed("The protected quarantine record could not be verified. Sentinel did not delete anything.");

            BrokerInvocationResult result = await _broker.DeleteFileAsync(record.ItemId, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
                return Failed($"Sentinel did not verify permanent deletion. {result.Message}", attempted: result.Code != "ElevationDenied");

            BrokerQuarantineRecord? remaining = await _broker.ReadProtectedRecordAsync(record.ItemId, cancellationToken).ConfigureAwait(false);
            return remaining is null
                ? new QuarantineResult(true, false, true, record, protectedRecord.Sha256,
                    "Sentinel permanently deleted the approved protected quarantine item and verified that its broker-owned record is gone.", true)
                : Failed("The broker reported deletion, but the protected quarantine record still exists. Sentinel did not report success.", attempted: true);
        }

        private async Task<BrokerQuarantineRecord?> GetMatchingProtectedRecordAsync(QuarantineRecord record, CancellationToken token)
        {
            if (!Guid.TryParseExact(record.ItemId, "N", out _)) return null;
            BrokerQuarantineRecord? protectedRecord = await _broker.ReadProtectedRecordAsync(record.ItemId, token).ConfigureAwait(false);
            if (protectedRecord is null) return null;

            if (!QuarantineRecordPathPolicy.TryCanonicalize(record.OriginalPath, out string original) ||
                !QuarantineRecordPathPolicy.TryCanonicalize(protectedRecord.OriginalPath, out string protectedOriginal))
                return null;

            return protectedOriginal.Equals(original, StringComparison.OrdinalIgnoreCase) &&
                   protectedRecord.Sha256.Equals(record.Sha256, StringComparison.OrdinalIgnoreCase)
                ? protectedRecord
                : null;
        }

        private static string ProtectedReference(string itemId) => "sentinel-quarantine://" + itemId;

        private static QuarantineResult Failed(string message, bool attempted = false) =>
            new(false, false, false, null, null, message, attempted);

        public sealed record QuarantineRecord(
            string OriginalPath,
            string QuarantinePath,
            string Sha256,
            DateTimeOffset QuarantinedAtUtc)
        {
            public string ItemId { get; init; } = string.Empty;
        }

        public sealed record QuarantineResult(
            bool Succeeded,
            bool RequiresUserApproval,
            bool Verified,
            QuarantineRecord? Record,
            string? Sha256,
            string Message,
            bool Attempted = false);
    }
}
