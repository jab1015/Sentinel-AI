/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Persists concise, user-safe records of verified maintenance and optimization
    /// outcomes. Technical details remain available for diagnostics, while normal
    /// UI surfaces can report only what changed and whether Sentinel verified it.
    /// </summary>
    public sealed class MaintenanceHistoryService
    {
        private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(30);
        private static readonly object HistorySync = new();
        private readonly string _historyPath;

        public MaintenanceHistoryService(string? historyPath = null)
        {
            if (string.IsNullOrWhiteSpace(historyPath))
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Modern Methods",
                    "Sentinel AI");

                Directory.CreateDirectory(directory);
                _historyPath = Path.Combine(directory, "maintenance-history.json");
                return;
            }

            _historyPath = Path.GetFullPath(historyPath);
            string? customDirectory = Path.GetDirectoryName(_historyPath);
            if (!string.IsNullOrWhiteSpace(customDirectory))
                Directory.CreateDirectory(customDirectory);
        }

        public void Record(MaintenanceHistoryEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            lock (HistorySync)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (!TryLoad(out List<MaintenanceHistoryEntry> loaded))
                    return;

                List<MaintenanceHistoryEntry> entries = loaded
                    .Where(item => now - item.TimestampUtc <= RetentionWindow)
                    .ToList();

                entries.Add(entry);
                Save(entries.OrderByDescending(item => item.TimestampUtc).Take(200).ToArray());
            }
        }

        public MaintenanceHistorySummary GetSummary()
        {
            lock (HistorySync)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (!TryLoad(out List<MaintenanceHistoryEntry> loaded))
                {
                    return new MaintenanceHistorySummary(
                        Array.Empty<MaintenanceHistoryEntry>(),
                        0,
                        0,
                        0,
                        0,
                        "Sentinel could not read the verified maintenance history. It will not infer that no maintenance occurred.",
                        HistoryAvailable: false);
                }

                MaintenanceHistoryEntry[] entries = loaded
                    .Where(item => now - item.TimestampUtc <= RetentionWindow)
                    .OrderByDescending(item => item.TimestampUtc)
                    .ToArray();

                int verified = entries.Count(item => item.Verified);
                int rolledBack = entries.Count(item => item.RolledBack);
                int failed = entries.Count(item => item.Attempted && !item.Successful && !item.RolledBack);

                string summary = entries.Length == 0
                    ? "Sentinel has not needed to perform any maintenance recently."
                    : failed > 0
                        ? $"Sentinel recorded {entries.Length} maintenance action(s) in the last 30 days. {verified} were verified and {failed} require follow-up."
                        : $"Sentinel recorded {entries.Length} maintenance action(s) in the last 30 days. {verified} were verified successfully{(rolledBack > 0 ? $" and {rolledBack} were safely rolled back" : string.Empty)}.";

                return new MaintenanceHistorySummary(
                    entries,
                    entries.Length,
                    verified,
                    rolledBack,
                    failed,
                    summary);
            }
        }

        private bool TryLoad(out List<MaintenanceHistoryEntry> entries)
        {
            entries = new List<MaintenanceHistoryEntry>();
            try
            {
                if (!File.Exists(_historyPath))
                    return true;

                string json = File.ReadAllText(_historyPath);
                List<MaintenanceHistoryEntry>? loaded =
                    JsonSerializer.Deserialize<List<MaintenanceHistoryEntry>>(json);
                if (loaded is null)
                    return false;

                entries = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void Save(IReadOnlyList<MaintenanceHistoryEntry> entries)
        {
            string temporaryPath = _historyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, _historyPath, true);
            }
            catch
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // Best-effort cleanup of this exact temporary file only.
                }

                // History persistence is informational only. A logging failure must
                // never change whether a maintenance action is considered safe.
            }
        }
    }

    public sealed record MaintenanceHistoryEntry(
        DateTimeOffset TimestampUtc,
        string Category,
        string Action,
        string UserSummary,
        bool Attempted,
        bool Successful,
        bool Verified,
        bool RolledBack,
        string TechnicalDetail);

    public sealed record MaintenanceHistorySummary(
        IReadOnlyList<MaintenanceHistoryEntry> Entries,
        int TotalActions,
        int VerifiedActions,
        int RolledBackActions,
        int FailedActions,
        string Summary,
        bool HistoryAvailable = true);
}
