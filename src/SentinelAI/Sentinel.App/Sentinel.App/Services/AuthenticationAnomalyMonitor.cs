/* Sentinel AI - Copyright (c) 2026 Modern Methods. */
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>Reads recent Windows failed-logon evidence and correlates repeated sources.</summary>
    public sealed class AuthenticationAnomalyMonitor
    {
        private const int MaximumEvents = 250;
        private const string FailedLogonQuery =
            "*[System[(EventID=4625) and TimeCreated[timediff(@SystemTime) <= 600000]]]";

        public AuthenticationAnomalySnapshot GetSnapshot()
        {
            List<FailedAuthenticationEvidence> evidence = new();
            try
            {
                EventLogQuery query = new("Security", PathType.LogName, FailedLogonQuery)
                {
                    ReverseDirection = true,
                    TolerateQueryErrors = true
                };
                using EventLogReader reader = new(query);
                for (int index = 0; index < MaximumEvents; index++)
                {
                    using EventRecord? record = reader.ReadEvent();
                    if (record is null) break;
                    evidence.Add(new FailedAuthenticationEvidence(
                        record.TimeCreated ?? DateTime.Now,
                        ReadProperty(record, 19, "Unknown source"),
                        ReadProperty(record, 5, "Unknown account")));
                }

                return Analyze(evidence, true);
            }
            catch (EventLogNotFoundException) { return Unavailable("The Windows Security log is unavailable."); }
            catch (UnauthorizedAccessException) { return Unavailable("Sentinel cannot read the Windows Security log without the required access."); }
            catch (EventLogException) { return Unavailable("Windows did not provide Security-log authentication evidence."); }
        }

        public static AuthenticationAnomalySnapshot Analyze(
            IReadOnlyCollection<FailedAuthenticationEvidence> evidence,
            bool collectionAvailable)
        {
            if (!collectionAvailable)
                return Unavailable("Windows Security-log authentication monitoring is unavailable.");

            FailedAuthenticationEvidence[] recent = evidence
                .Where(item => item.Timestamp >= DateTime.Now.AddMinutes(-10))
                .ToArray();
            FailedAuthenticationEvidence[] remote = recent
                .Where(item => IsRemoteSource(item.SourceAddress))
                .ToArray();
            var repeatedSource = remote
                .GroupBy(item => item.SourceAddress, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .FirstOrDefault();
            int repeatedCount = repeatedSource?.Count() ?? 0;
            bool suspicious = repeatedCount >= 5 || remote.Length >= 10;
            int confidence = suspicious
                ? Math.Min(95, 60 + Math.Max(repeatedCount - 4, remote.Length - 9) * 5)
                : recent.Length == 0 ? 100 : Math.Min(45, remote.Length * 8);
            string source = repeatedSource?.Key ?? "None";
            string summary = suspicious
                ? repeatedCount >= 5
                    ? $"Sentinel verified {repeatedCount} failed logons from {source} within 10 minutes. This repeated remote pattern requires investigation."
                    : $"Sentinel verified {remote.Length} failed logons from remote sources within 10 minutes across multiple sources."
                : recent.Length == 0
                    ? "No failed-logon authentication events were detected in the last 10 minutes."
                    : remote.Length == 0
                        ? $"Sentinel observed {recent.Length} recent local failed logon(s); no remote brute-force pattern is present."
                        : $"Sentinel observed {recent.Length} recent failed logon(s), including {remote.Length} from remote sources; the evidence does not currently form a brute-force pattern.";

            return new AuthenticationAnomalySnapshot(true, recent.Length, repeatedCount, source,
                suspicious, confidence, suspicious ? "Suspicious" : recent.Length > 0 ? "Observing" : "Healthy", summary);
        }

        private static string ReadProperty(EventRecord record, int index, string fallback)
        {
            if (record.Properties.Count <= index) return fallback;
            string? value = record.Properties[index].Value?.ToString();
            return string.IsNullOrWhiteSpace(value) || value == "-" ? fallback : value;
        }

        private static bool IsRemoteSource(string value) =>
            !string.IsNullOrWhiteSpace(value) && value != "-" && value != "::1" &&
            value != "127.0.0.1" && !value.Equals("Unknown source", StringComparison.OrdinalIgnoreCase);

        private static AuthenticationAnomalySnapshot Unavailable(string summary) =>
            new(false, 0, 0, "None", false, 0, "Unavailable", summary);

        public sealed record FailedAuthenticationEvidence(DateTime Timestamp, string SourceAddress, string AccountName);
        public sealed record AuthenticationAnomalySnapshot(bool CollectionAvailable, int FailedLogonCount,
            int RepeatedSourceFailureCount, string PrimarySourceAddress, bool SuspiciousPattern,
            int ConfidenceScore, string State, string Summary);
    }
}
