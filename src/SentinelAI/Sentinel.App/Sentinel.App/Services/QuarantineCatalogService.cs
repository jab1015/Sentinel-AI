/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Persists the minimal verified metadata Sentinel needs to present and safely
    /// restore quarantined files across application restarts. The catalog contains
    /// no executable content; files remain isolated in the quarantine directory.
    /// </summary>
    public sealed class QuarantineCatalogService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly string _catalogPath;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public QuarantineCatalogService(string? catalogPath = null)
        {
            _catalogPath = catalogPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SentinelAI",
                "Quarantine",
                "catalog.json");
        }

        public async Task<IReadOnlyList<QuarantineCatalogEntry>> GetEntriesAsync(
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<QuarantineCatalogEntry> entries = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
                return entries
                    .OrderByDescending(entry => entry.QuarantinedAtUtc)
                    .ToArray();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task AddAsync(
            QuarantineService.QuarantineRecord record,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<QuarantineCatalogEntry> entries = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
                entries.RemoveAll(entry =>
                    string.Equals(entry.QuarantinePath, record.QuarantinePath, StringComparison.OrdinalIgnoreCase));

                entries.Add(new QuarantineCatalogEntry(
                    record.OriginalPath,
                    record.QuarantinePath,
                    record.Sha256,
                    record.QuarantinedAtUtc,
                    File.Exists(record.QuarantinePath)));

                await WriteUnsafeAsync(entries, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task RemoveAsync(
            string quarantinePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(quarantinePath)) return;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<QuarantineCatalogEntry> entries = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
                entries.RemoveAll(entry =>
                    string.Equals(entry.QuarantinePath, quarantinePath, StringComparison.OrdinalIgnoreCase));
                await WriteUnsafeAsync(entries, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<IReadOnlyList<QuarantineCatalogEntry>> ReconcileAsync(
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<QuarantineCatalogEntry> entries = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
                bool changed = false;

                for (int index = 0; index < entries.Count; index++)
                {
                    bool exists = File.Exists(entries[index].QuarantinePath);
                    if (entries[index].IsPresent == exists) continue;
                    entries[index] = entries[index] with { IsPresent = exists };
                    changed = true;
                }

                if (changed)
                {
                    await WriteUnsafeAsync(entries, cancellationToken).ConfigureAwait(false);
                }

                return entries
                    .OrderByDescending(entry => entry.QuarantinedAtUtc)
                    .ToArray();
            }
            finally
            {
                _gate.Release();
            }
        }

        public QuarantineService.QuarantineRecord ToRecord(QuarantineCatalogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return new QuarantineService.QuarantineRecord(
                entry.OriginalPath,
                entry.QuarantinePath,
                entry.Sha256,
                entry.QuarantinedAtUtc);
        }

        private async Task<List<QuarantineCatalogEntry>> ReadUnsafeAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_catalogPath)) return new List<QuarantineCatalogEntry>();

            try
            {
                await using FileStream stream = new(
                    _catalogPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                return await JsonSerializer.DeserializeAsync<List<QuarantineCatalogEntry>>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false) ?? new List<QuarantineCatalogEntry>();
            }
            catch (JsonException)
            {
                // A malformed catalog must never cause Sentinel to trust or act on
                // unverified quarantine metadata. Start with an empty in-memory view.
                return new List<QuarantineCatalogEntry>();
            }
        }

        private async Task WriteUnsafeAsync(
            List<QuarantineCatalogEntry> entries,
            CancellationToken cancellationToken)
        {
            string? directory = Path.GetDirectoryName(_catalogPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            string temporaryPath = _catalogPath + ".tmp";
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _catalogPath, overwrite: true);
        }

        public sealed record QuarantineCatalogEntry(
            string OriginalPath,
            string QuarantinePath,
            string Sha256,
            DateTimeOffset QuarantinedAtUtc,
            bool IsPresent)
        {
            public string FileName => Path.GetFileName(OriginalPath);
        }
    }
}
