/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    public sealed class QuarantineCatalogService
    {
        private readonly PrivilegedBrokerClient _broker = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<string, Annotation> _annotations = new(StringComparer.OrdinalIgnoreCase);

        public QuarantineCatalogService(string? catalogPath = null)
        {
            // Retained for source compatibility. Broker records are authoritative.
        }

        public async Task<IReadOnlyList<QuarantineCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                BrokerQuarantineRecord[] records = await _broker.ReadProtectedRecordsAsync(cancellationToken).ConfigureAwait(false);
                return records
                    .Where(r => Guid.TryParseExact(r.ItemId, "N", out _))
                    .Select(r =>
                    {
                        _annotations.TryGetValue(r.ItemId, out Annotation? annotation);
                        return new QuarantineCatalogEntry(
                            r.OriginalPath,
                            BuildReference(r.ItemId),
                            r.Sha256,
                            r.QuarantinedAtUtc,
                            true,
                            annotation?.ReasonCode ?? string.Empty,
                            annotation?.EvidenceConfidencePercent ?? 0,
                            r.ItemId);
                    })
                    .OrderByDescending(r => r.QuarantinedAtUtc)
                    .ToArray();
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task AddAsync(QuarantineService.QuarantineRecord record, CancellationToken cancellationToken = default) =>
            AddAsync(record, string.Empty, 0, cancellationToken);

        public async Task AddAsync(
            QuarantineService.QuarantineRecord record,
            string reasonCode,
            int evidenceConfidencePercent,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!Guid.TryParseExact(record.ItemId, "N", out _))
                throw new InvalidOperationException("The quarantine item identifier is invalid.");

            BrokerQuarantineRecord? brokerRecord = await _broker.ReadProtectedRecordAsync(record.ItemId, cancellationToken).ConfigureAwait(false);
            if (brokerRecord is null ||
                !string.Equals(brokerRecord.Sha256, record.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !PathsEqual(brokerRecord.OriginalPath, record.OriginalPath))
            {
                throw new InvalidOperationException("The quarantine item could not be verified against the broker record.");
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _annotations[record.ItemId] = new Annotation(
                    reasonCode ?? string.Empty,
                    Math.Clamp(evidenceConfidencePercent, 0, 100));
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task RemoveAsync(string quarantineReference, CancellationToken cancellationToken = default)
        {
            string itemId = ExtractItemId(quarantineReference);
            if (!string.IsNullOrWhiteSpace(itemId)) _annotations.Remove(itemId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<QuarantineCatalogEntry>> ReconcileAsync(CancellationToken cancellationToken = default) =>
            GetEntriesAsync(cancellationToken);

        public QuarantineService.QuarantineRecord ToRecord(QuarantineCatalogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!Guid.TryParseExact(entry.ItemId, "N", out _))
                throw new InvalidOperationException("The quarantine entry identifier is invalid.");

            return new QuarantineService.QuarantineRecord(
                entry.OriginalPath,
                BuildReference(entry.ItemId),
                entry.Sha256,
                entry.QuarantinedAtUtc)
            {
                ItemId = entry.ItemId
            };
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildReference(string itemId) => "sentinel-quarantine://" + itemId;

        private static string ExtractItemId(string value)
        {
            const string prefix = "sentinel-quarantine://";
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            string raw = value[prefix.Length..];
            return Guid.TryParseExact(raw, "N", out Guid parsed) ? parsed.ToString("N") : string.Empty;
        }

        private sealed record Annotation(string ReasonCode, int EvidenceConfidencePercent);

        public sealed record QuarantineCatalogEntry(
            string OriginalPath,
            string QuarantinePath,
            string Sha256,
            DateTimeOffset QuarantinedAtUtc,
            bool IsPresent,
            string ReasonCode = "",
            int EvidenceConfidencePercent = 0,
            string ItemId = "")
        {
            public string FileName => Path.GetFileName(OriginalPath);
        }
    }
}
