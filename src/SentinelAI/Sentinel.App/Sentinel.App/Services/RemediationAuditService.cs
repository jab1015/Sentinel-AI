/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Persists a local, append-only audit record of remediation decisions and
    /// verified outcomes. The audit contains no secrets and is stored under the
    /// current user's LocalApplicationData folder.
    /// </summary>
    public sealed class RemediationAuditService
    {
        private readonly string _auditPath;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public RemediationAuditService(string? auditPath = null)
        {
            _auditPath = auditPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SentinelAI",
                "Audit",
                "remediation-history.jsonl");
        }

        public async Task RecordAsync(
            string action,
            string target,
            bool userApproved,
            bool succeeded,
            bool verified,
            string message,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new ArgumentException("An audit action is required.", nameof(action));
            }

            var entry = new RemediationAuditEntry(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                action.Trim(),
                SanitizeTarget(target),
                userApproved,
                succeeded,
                verified,
                message?.Trim() ?? string.Empty);

            string json = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string? directory = Path.GetDirectoryName(_auditPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.AppendAllTextAsync(_auditPath, json, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<IReadOnlyList<RemediationAuditEntry>> ReadRecentAsync(
            int maximumEntries = 100,
            CancellationToken cancellationToken = default)
        {
            if (maximumEntries <= 0)
            {
                return Array.Empty<RemediationAuditEntry>();
            }

            if (!File.Exists(_auditPath))
            {
                return Array.Empty<RemediationAuditEntry>();
            }

            string[] lines = await File.ReadAllLinesAsync(_auditPath, cancellationToken).ConfigureAwait(false);
            var results = new List<RemediationAuditEntry>(Math.Min(maximumEntries, lines.Length));

            for (int index = lines.Length - 1; index >= 0 && results.Count < maximumEntries; index--)
            {
                if (string.IsNullOrWhiteSpace(lines[index]))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<RemediationAuditEntry>(lines[index], JsonOptions);
                    if (entry is not null)
                    {
                        results.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // A damaged historical line must not prevent Sentinel from
                    // reading later valid audit records.
                }
            }

            return results;
        }

        private static string SanitizeTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return "Unspecified";
            }

            string trimmed = target.Trim();
            return trimmed.Length <= 512 ? trimmed : trimmed[..512];
        }

        public sealed record RemediationAuditEntry(
            Guid Id,
            DateTimeOffset TimestampUtc,
            string Action,
            string Target,
            bool UserApproved,
            bool Succeeded,
            bool Verified,
            string Message);
    }
}
