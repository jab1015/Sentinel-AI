/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    public sealed class InvestigationHistoryService
    {
        private const long MaximumHistoryBytes = 8 * 1024 * 1024;
        private const int MaximumRecordBytes = 32 * 1024;
        private const long RetainedHistoryBytes = 4 * 1024 * 1024;
        private readonly string _historyPath;
        private static readonly SemaphoreSlim HistoryLock = new(1, 1);
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public InvestigationHistoryService(string? historyPath = null)
        {
            _historyPath = Path.GetFullPath(historyPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SentinelAI", "History", "investigations.jsonl"));
        }

        public async Task RecordAsync(string fingerprint, string title, string conclusion, string severity,
            bool requiresAttention, bool resolved, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fingerprint)) return;

            var entry = new InvestigationHistoryEntry(
                Guid.NewGuid(), DateTimeOffset.UtcNow, fingerprint.Trim(), title?.Trim() ?? string.Empty,
                conclusion?.Trim() ?? string.Empty, severity?.Trim() ?? string.Empty, requiresAttention, resolved);

            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
            if (serialized.Length > MaximumRecordBytes)
                throw new InvalidOperationException("The investigation record exceeds Sentinel's bounded history-record size.");

            await HistoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string? directory = Path.GetDirectoryName(_historyPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                await RotateIfNeededAsync(serialized.Length + Environment.NewLine.Length, cancellationToken).ConfigureAwait(false);

                await using FileStream stream = new(_historyPath, FileMode.Append, FileAccess.Write, FileShare.Read,
                    8192, FileOptions.Asynchronous | FileOptions.WriteThrough);
                await stream.WriteAsync(serialized, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(Environment.NewLine), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { HistoryLock.Release(); }
        }

        public async Task<IReadOnlyList<InvestigationHistoryEntry>> ReadRecentAsync(
            int maximumEntries = 100, CancellationToken cancellationToken = default)
        {
            if (maximumEntries <= 0) return Array.Empty<InvestigationHistoryEntry>();
            maximumEntries = Math.Min(maximumEntries, 2_000);

            await HistoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(_historyPath)) return Array.Empty<InvestigationHistoryEntry>();
                return await ReadTailAsync(maximumEntries, cancellationToken).ConfigureAwait(false);
            }
            finally { HistoryLock.Release(); }
        }

        public async Task<int> CountRecentOccurrencesAsync(string fingerprint, TimeSpan window,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fingerprint) || window <= TimeSpan.Zero) return 0;
            IReadOnlyList<InvestigationHistoryEntry> entries = await ReadRecentAsync(500, cancellationToken).ConfigureAwait(false);
            DateTimeOffset cutoff = DateTimeOffset.UtcNow.Subtract(window);
            int count = 0;
            foreach (var entry in entries)
                if (entry.TimestampUtc >= cutoff && string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)) count++;
            return count;
        }

        private async Task RotateIfNeededAsync(long incomingBytes, CancellationToken token)
        {
            if (!File.Exists(_historyPath)) return;
            FileInfo info = new(_historyPath);
            if (info.Length + incomingBytes <= MaximumHistoryBytes) return;

            long start = Math.Max(0, info.Length - RetainedHistoryBytes);
            string temp = _historyPath + ".rotate-" + Guid.NewGuid().ToString("N") + ".tmp";
            await using (FileStream input = new(_historyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (FileStream output = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                input.Seek(start, SeekOrigin.Begin);
                if (start > 0) await SkipToNextLineAsync(input, token).ConfigureAwait(false);
                await input.CopyToAsync(output, 81920, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            File.Move(temp, _historyPath, overwrite: true);
        }

        private async Task<IReadOnlyList<InvestigationHistoryEntry>> ReadTailAsync(int maximumEntries, CancellationToken token)
        {
            await using FileStream stream = new(_historyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192,
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            long length = stream.Length;
            if (length == 0) return Array.Empty<InvestigationHistoryEntry>();

            const int blockSize = 16 * 1024;
            byte[] rented = ArrayPool<byte>.Shared.Rent(blockSize);
            List<byte[]> lines = new(maximumEntries);
            List<byte> current = new(MaximumRecordBytes);
            try
            {
                long position = length;
                while (position > 0 && lines.Count < maximumEntries)
                {
                    token.ThrowIfCancellationRequested();
                    int toRead = (int)Math.Min(blockSize, position);
                    position -= toRead;
                    stream.Seek(position, SeekOrigin.Begin);
                    int read = await stream.ReadAsync(rented.AsMemory(0, toRead), token).ConfigureAwait(false);
                    for (int i = read - 1; i >= 0; i--)
                    {
                        byte b = rented[i];
                        if (b == (byte)'\n')
                        {
                            if (current.Count > 0)
                            {
                                current.Reverse();
                                lines.Add(current.ToArray());
                                current.Clear();
                                if (lines.Count >= maximumEntries) break;
                            }
                        }
                        else if (b != (byte)'\r' && current.Count < MaximumRecordBytes)
                        {
                            current.Add(b);
                        }
                        else if (current.Count >= MaximumRecordBytes)
                        {
                            // Oversized/corrupt historical record is skipped without unbounded allocation.
                        }
                    }
                }
                if (current.Count > 0 && lines.Count < maximumEntries)
                {
                    current.Reverse();
                    lines.Add(current.ToArray());
                }

                var results = new List<InvestigationHistoryEntry>(lines.Count);
                foreach (byte[] line in lines)
                {
                    try
                    {
                        InvestigationHistoryEntry? entry = JsonSerializer.Deserialize<InvestigationHistoryEntry>(line, JsonOptions);
                        if (entry is not null) results.Add(entry);
                    }
                    catch (JsonException) { }
                }
                return results;
            }
            finally { ArrayPool<byte>.Shared.Return(rented); }
        }

        private static async Task SkipToNextLineAsync(Stream stream, CancellationToken token)
        {
            byte[] one = new byte[1];
            while (await stream.ReadAsync(one, token).ConfigureAwait(false) == 1)
                if (one[0] == (byte)'\n') break;
        }

        public sealed record InvestigationHistoryEntry(Guid Id, DateTimeOffset TimestampUtc, string Fingerprint,
            string Title, string Conclusion, string Severity, bool RequiresAttention, bool Resolved);
    }
}
