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
        private readonly string _historyPath;

        public MaintenanceHistoryService()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Modern Methods",
                "Sentinel AI");

            Directory.CreateDirectory(directory);
            _historyPath = Path.Combine(directory, "maintenance-history.json");
        }

        public void Record(MaintenanceHistoryEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<MaintenanceHistoryEntry> entries = Load()
                .Where(item => now - item.TimestampUtc <= RetentionWindow)
                .ToList();

            entries.Add(entry);
            Save(entries.OrderByDescending(item => item.TimestampUtc).Take(200).ToArray());
        }

        public MaintenanceHistorySummary GetSummary()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            MaintenanceHistoryEntry[] entries = Load()
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

        private List<MaintenanceHistoryEntry> Load()
        {
            try
            {
                if (!File.Exists(_historyPath))
                    return new List<MaintenanceHistoryEntry>();

                string json = File.ReadAllText(_historyPath);
                return JsonSerializer.Deserialize<List<MaintenanceHistoryEntry>>(json)
                    ?? new List<MaintenanceHistoryEntry>();
            }
            catch
            {
                return new List<MaintenanceHistoryEntry>();
            }
        }

        private void Save(IReadOnlyList<MaintenanceHistoryEntry> entries)
        {
            try
            {
                string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_historyPath, json);
            }
            catch
            {
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
        string Summary);
}
