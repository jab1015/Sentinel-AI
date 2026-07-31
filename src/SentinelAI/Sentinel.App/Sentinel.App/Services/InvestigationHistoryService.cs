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
    /// Stores completed investigation conclusions locally so recurrence and
    /// resolution history can be evaluated without interrupting healthy users.
    /// </summary>
    public sealed class InvestigationHistoryService
    {
        private readonly string _historyPath;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public InvestigationHistoryService(string? historyPath = null)
        {
            _historyPath = historyPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SentinelAI",
                "History",
                "investigations.jsonl");
        }

        public async Task RecordAsync(
            string fingerprint,
            string title,
            string conclusion,
            string severity,
            bool requiresAttention,
            bool resolved,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                return;
            }

            var entry = new InvestigationHistoryEntry(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                fingerprint.Trim(),
                title?.Trim() ?? string.Empty,
                conclusion?.Trim() ?? string.Empty,
                severity?.Trim() ?? string.Empty,
                requiresAttention,
                resolved);

            string json = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string? directory = Path.GetDirectoryName(_historyPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.AppendAllTextAsync(_historyPath, json, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<IReadOnlyList<InvestigationHistoryEntry>> ReadRecentAsync(
            int maximumEntries = 100,
            CancellationToken cancellationToken = default)
        {
            if (maximumEntries <= 0 || !File.Exists(_historyPath))
            {
                return Array.Empty<InvestigationHistoryEntry>();
            }

            string[] lines = await File.ReadAllLinesAsync(_historyPath, cancellationToken).ConfigureAwait(false);
            var results = new List<InvestigationHistoryEntry>(Math.Min(maximumEntries, lines.Length));

            for (int index = lines.Length - 1; index >= 0 && results.Count < maximumEntries; index--)
            {
                if (string.IsNullOrWhiteSpace(lines[index]))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<InvestigationHistoryEntry>(lines[index], JsonOptions);
                    if (entry is not null)
                    {
                        results.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // Preserve access to valid history if one historical record is damaged.
                }
            }

            return results;
        }

        public async Task<int> CountRecentOccurrencesAsync(
            string fingerprint,
            TimeSpan window,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fingerprint) || window <= TimeSpan.Zero)
            {
                return 0;
            }

            IReadOnlyList<InvestigationHistoryEntry> entries =
                await ReadRecentAsync(500, cancellationToken).ConfigureAwait(false);

            DateTimeOffset cutoff = DateTimeOffset.UtcNow.Subtract(window);
            int count = 0;

            foreach (var entry in entries)
            {
                if (entry.TimestampUtc < cutoff)
                {
                    continue;
                }

                if (string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        public sealed record InvestigationHistoryEntry(
            Guid Id,
            DateTimeOffset TimestampUtc,
            string Fingerprint,
            string Title,
            string Conclusion,
            string Severity,
            bool RequiresAttention,
            bool Resolved);
    }
}
