/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics.Eventing.Reader;

namespace Sentinel.App.Services
{
    public sealed class EventLogMonitor
    {
        private const int MaximumEventsPerLog = 200;
        private const string RecentCriticalAndErrorQuery =
            "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 86400000]]]";

        public EventLogStatusSnapshot GetStatus()
        {
            int criticalCount = 0;
            int errorCount = 0;
            DateTime? latestEventTime = null;
            string latestEventSource = "None";
            string latestEventMessage = "No Windows critical/error event evidence was detected.";

            bool systemLogAvailable = ReadLog("System", ref criticalCount, ref errorCount,
                ref latestEventTime, ref latestEventSource, ref latestEventMessage);
            bool applicationLogAvailable = ReadLog("Application", ref criticalCount, ref errorCount,
                ref latestEventTime, ref latestEventSource, ref latestEventMessage);

            bool collectionAvailable = systemLogAvailable && applicationLogAvailable;
            if (!collectionAvailable && criticalCount == 0 && errorCount == 0)
            {
                latestEventSource = "Unavailable";
                latestEventMessage = "Windows critical/error event evidence could not be collected completely.";
            }

            return new EventLogStatusSnapshot(criticalCount, errorCount, latestEventTime,
                latestEventSource, latestEventMessage, collectionAvailable);
        }

        private static bool ReadLog(
            string logName,
            ref int criticalCount,
            ref int errorCount,
            ref DateTime? latestEventTime,
            ref string latestEventSource,
            ref string latestEventMessage)
        {
            try
            {
                EventLogQuery query = new(logName, PathType.LogName, RecentCriticalAndErrorQuery)
                {
                    ReverseDirection = true,
                    TolerateQueryErrors = true
                };

                using EventLogReader reader = new(query);
                for (int index = 0; index < MaximumEventsPerLog; index++)
                {
                    using EventRecord? record = reader.ReadEvent();
                    if (record is null) break;

                    string description = GetSafeDescription(record);
                    if (IsKnownBenign(record.ProviderName ?? string.Empty, description)) continue;

                    // Count unrelated critical/error evidence before any later classification.
                    // Event-log severity alone does not prove a security incident, but dropping
                    // these records would make the aggregate evidence false.
                    if (record.Level == 1) criticalCount++;
                    else if (record.Level == 2) errorCount++;
                    else continue;

                    DateTime? eventTime = record.TimeCreated;
                    if (eventTime.HasValue && (!latestEventTime.HasValue || eventTime.Value > latestEventTime.Value))
                    {
                        latestEventTime = eventTime;
                        latestEventSource = string.IsNullOrWhiteSpace(record.ProviderName) ? logName : record.ProviderName;
                        latestEventMessage = description;
                    }
                }

                return true;
            }
            catch (EventLogNotFoundException) { return false; }
            catch (EventLogException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static bool IsKnownBenign(string provider, string description)
        {
            if (provider.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase) &&
                description.Contains("Microsoft Storage Spaces SMP", StringComparison.OrdinalIgnoreCase))
                return true;

            if (provider.Contains("WindowsUpdateClient", StringComparison.OrdinalIgnoreCase) &&
                description.Contains("0x80073D02", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string GetSafeDescription(EventRecord record)
        {
            try
            {
                string? description = record.FormatDescription();
                return string.IsNullOrWhiteSpace(description) ? $"Event ID {record.Id}" : Normalize(description);
            }
            catch (EventLogException) { return $"Event ID {record.Id}"; }
        }

        private static string Normalize(string value)
        {
            string normalized = value.Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal).Trim();
            return normalized.Length <= 240 ? normalized : normalized[..237] + "...";
        }

        public sealed record EventLogStatusSnapshot(
            int CriticalCount,
            int ErrorCount,
            DateTime? LatestEventTime,
            string LatestEventSource,
            string LatestEventMessage,
            bool CollectionAvailable = true)
        {
            public static EventLogStatusSnapshot Unavailable { get; } =
                new(0, 0, null, "Unavailable", "Windows critical/error event evidence could not be collected.", false);
        }
    }
}
