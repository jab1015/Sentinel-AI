/* Sentinel AI - Copyright (c) 2026 Modern Methods. */
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sentinel.App.Services
{
    /// <summary>Collects bounded Windows crash evidence without reading or uploading dump contents.</summary>
    public sealed class WindowsCrashEvidenceMonitor
    {
        private const int MaximumEvents = 40;
        private const string CrashEventQuery =
            "*[System[(((EventID=1001) and Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting']) or (EventID=41) or (EventID=6008)) and TimeCreated[timediff(@SystemTime) <= 604800000]]]";

        public WindowsCrashEvidenceSnapshot GetSnapshot()
        {
            List<CrashEventEvidence> events = new();
            try
            {
                EventLogQuery query = new("System", PathType.LogName, CrashEventQuery)
                {
                    ReverseDirection = true,
                    TolerateQueryErrors = true
                };
                using EventLogReader reader = new(query);
                for (int index = 0; index < MaximumEvents; index++)
                {
                    using EventRecord? record = reader.ReadEvent();
                    if (record is null) break;
                    events.Add(new CrashEventEvidence(
                        record.Id,
                        record.TimeCreated ?? DateTime.MinValue,
                        record.ProviderName ?? "Windows",
                        GetSafeDescription(record)));
                }

                return Analyze(events, FindLatestMinidump(), true);
            }
            catch (EventLogNotFoundException) { return Unavailable("The Windows System event log is unavailable."); }
            catch (UnauthorizedAccessException) { return Unavailable("Sentinel cannot access Windows crash-event evidence."); }
            catch (EventLogException) { return Unavailable("Windows did not provide crash-event evidence during this check."); }
        }

        public static WindowsCrashEvidenceSnapshot Analyze(
            IReadOnlyCollection<CrashEventEvidence> events,
            MinidumpEvidence? minidump,
            bool collectionAvailable)
        {
            if (!collectionAvailable) return Unavailable("Windows crash evidence is unavailable.");

            CrashEventEvidence? bugCheck = events.Where(item =>
                    item.EventId == 1001 &&
                    (item.Provider.Equals("Microsoft-Windows-WER-SystemErrorReporting", StringComparison.OrdinalIgnoreCase) ||
                     item.Description.Contains("bugcheck", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(item => item.Timestamp).FirstOrDefault();
            CrashEventEvidence? restart = events.Where(item => item.EventId is 41 or 6008)
                .OrderByDescending(item => item.Timestamp).FirstOrDefault();
            CrashEventEvidence? primary = bugCheck ?? restart;
            if (primary is null && minidump is null)
                return new(true, false, false, null, 0, "None", "None", false,
                    "Sentinel found no Windows crash event or recent minidump in the last 7 days.");

            DateTime? occurredAt = new[] { primary?.Timestamp, minidump?.LastWriteTime }
                .Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty().Max();
            string bugCheckCode = bugCheck is null ? "Not available" : ExtractBugCheckCode(bugCheck.Description);
            bool rootCauseVerified = false;
            string summary;
            if (bugCheck is not null)
            {
                summary = $"Windows recorded a blue-screen BugCheck event{(bugCheckCode == "Not available" ? string.Empty : $" with stop code {bugCheckCode}")}. " +
                    "This verifies that a system crash occurred, but the event alone does not identify the responsible driver or hardware component.";
            }
            else
            {
                summary = "Windows recorded an unexpected shutdown or restart. This confirms an abnormal interruption, but it does not prove that a blue screen occurred or identify its cause.";
            }

            if (minidump is not null)
                summary += " A recent Windows minidump is present for deeper local analysis; Sentinel has not read or uploaded its contents.";

            return new(true, true, bugCheck is not null, occurredAt, primary?.EventId ?? 0,
                primary?.Provider ?? "Minidump", bugCheckCode, rootCauseVerified, summary);
        }

        private static MinidumpEvidence? FindLatestMinidump()
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
                if (!Directory.Exists(directory)) return null;
                FileInfo? latest = new DirectoryInfo(directory).EnumerateFiles("*.dmp", SearchOption.TopDirectoryOnly)
                    .Where(file => file.LastWriteTime >= DateTime.Now.AddDays(-7))
                    .OrderByDescending(file => file.LastWriteTime).FirstOrDefault();
                return latest is null ? null : new MinidumpEvidence(latest.LastWriteTime, latest.Length);
            }
            catch { return null; }
        }

        private static string GetSafeDescription(EventRecord record)
        {
            try { return Normalize(record.FormatDescription() ?? $"Event ID {record.Id}"); }
            catch (EventLogException) { return $"Event ID {record.Id}"; }
        }

        private static string ExtractBugCheckCode(string value)
        {
            Match match = Regex.Match(value ?? string.Empty, @"0x[0-9a-fA-F]{1,16}");
            return match.Success ? match.Value.ToUpperInvariant() : "Not available";
        }

        private static string Normalize(string value)
        {
            string normalized = Regex.Replace(value, @"\s+", " ").Trim();
            return normalized.Length <= 500 ? normalized : normalized[..497] + "...";
        }

        private static WindowsCrashEvidenceSnapshot Unavailable(string summary) =>
            new(false, false, false, null, 0, "Unavailable", "Not available", false, summary);

        public sealed record CrashEventEvidence(int EventId, DateTime Timestamp, string Provider, string Description);
        public sealed record MinidumpEvidence(DateTime LastWriteTime, long Length);
        public sealed record WindowsCrashEvidenceSnapshot(bool CollectionAvailable, bool CrashDetected,
            bool BugCheckDetected, DateTime? OccurredAt, int PrimaryEventId, string Provider,
            string BugCheckCode, bool RootCauseVerified, string Summary);
    }
}

