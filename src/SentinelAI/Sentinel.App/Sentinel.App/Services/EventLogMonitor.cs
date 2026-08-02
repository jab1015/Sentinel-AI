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
            string latestEventMessage = "No critical or error events detected in the last 24 hours.";

            ReadLog(
                "System",
                ref criticalCount,
                ref errorCount,
                ref latestEventTime,
                ref latestEventSource,
                ref latestEventMessage);

            ReadLog(
                "Application",
                ref criticalCount,
                ref errorCount,
                ref latestEventTime,
                ref latestEventSource,
                ref latestEventMessage);

            return new EventLogStatusSnapshot(
                criticalCount,
                errorCount,
                latestEventTime,
                latestEventSource,
                latestEventMessage);
        }

        private static void ReadLog(
            string logName,
            ref int criticalCount,
            ref int errorCount,
            ref DateTime? latestEventTime,
            ref string latestEventSource,
            ref string latestEventMessage)
        {
            try
            {
                EventLogQuery query = new(
                    logName,
                    PathType.LogName,
                    RecentCriticalAndErrorQuery)
                {
                    ReverseDirection = true,
                    TolerateQueryErrors = true
                };

                using EventLogReader reader = new(query);

                for (int index = 0; index < MaximumEventsPerLog; index++)
                {
                    using EventRecord? record = reader.ReadEvent();
                    if (record is null)
                    {
                        break;
                    }

                    // DistributedCOM event 10010 is commonly emitted when a COM server does
                    // not register before Windows' timeout. By itself it is not evidence of a
                    // security incident or a user-actionable reliability problem, so Sentinel
                    // keeps it as Windows diagnostic noise rather than elevating it solely
                    // because Event Viewer classifies it as an error.
                    if (IsRoutineDistributedComTimeout(record))
                    {
                        continue;
                    }

                    if (record.Level == 1)
                    {
                        criticalCount++;
                    }
                    else if (record.Level == 2)
                    {
                        errorCount++;
                    }

                    DateTime? eventTime = record.TimeCreated;
                    if (eventTime.HasValue &&
                        (!latestEventTime.HasValue || eventTime.Value > latestEventTime.Value))
                    {
                        latestEventTime = eventTime;
                        latestEventSource = string.IsNullOrWhiteSpace(record.ProviderName)
                            ? logName
                            : record.ProviderName;
                        latestEventMessage = GetSafeDescription(record);
                    }
                }
            }
            catch (EventLogNotFoundException)
            {
                // The log is unavailable on this Windows installation.
            }
            catch (EventLogException)
            {
                // Access or provider failures must not terminate monitoring.
            }
            catch (UnauthorizedAccessException)
            {
                // Some event channels require elevation. Continue gracefully.
            }
        }

        private static bool IsRoutineDistributedComTimeout(EventRecord record)
        {
            return record.Id == 10010 &&
                !string.IsNullOrWhiteSpace(record.ProviderName) &&
                record.ProviderName.Contains("DistributedCOM", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSafeDescription(EventRecord record)
        {
            try
            {
                string? description = record.FormatDescription();
                return string.IsNullOrWhiteSpace(description)
                    ? $"Event ID {record.Id}"
                    : Normalize(description);
            }
            catch (EventLogException)
            {
                return $"Event ID {record.Id}";
            }
        }

        private static string Normalize(string value)
        {
            string normalized = value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();

            return normalized.Length <= 240
                ? normalized
                : normalized[..237] + "...";
        }

        public sealed record EventLogStatusSnapshot(
            int CriticalCount,
            int ErrorCount,
            DateTime? LatestEventTime,
            string LatestEventSource,
            string LatestEventMessage);
    }
}
