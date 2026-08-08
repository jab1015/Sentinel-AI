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
            string latestEventMessage = "No user-actionable Windows event evidence was detected.";

            bool systemLogAvailable = ReadLog(
                "System",
                ref criticalCount,
                ref errorCount,
                ref latestEventTime,
                ref latestEventSource,
                ref latestEventMessage);

            bool applicationLogAvailable = ReadLog(
                "Application",
                ref criticalCount,
                ref errorCount,
                ref latestEventTime,
                ref latestEventSource,
                ref latestEventMessage);

            bool collectionAvailable = systemLogAvailable && applicationLogAvailable;
            if (!collectionAvailable && criticalCount == 0 && errorCount == 0)
            {
                latestEventSource = "Unavailable";
                latestEventMessage = "Windows critical/error event evidence could not be collected completely.";
            }

            return new EventLogStatusSnapshot(
                criticalCount,
                errorCount,
                latestEventTime,
                latestEventSource,
                latestEventMessage,
                collectionAvailable);
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

                    string description = GetSafeDescription(record);
                    if (!IsUserActionable(record))
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
                        latestEventMessage = description;
                    }
                }

                return true;
            }
            catch (EventLogNotFoundException)
            {
                return false;
            }
            catch (EventLogException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool IsUserActionable(EventRecord record)
        {
            // Sentinel does not elevate ordinary Windows "Error" entries to the user simply
            // because Event Viewer labels them errors. Level-2 events remain diagnostic context
            // for internal investigation and must be corroborated by a current process, service,
            // security, resource, persistence, or network condition before the UI asks the user
            // to do anything. This prevents routine DCOM, SCEP, licensing, Bluetooth, camera,
            // device-state, update, and service lifecycle noise from becoming false alarms.
            if (record.Level == 2)
            {
                return false;
            }

            // Critical Windows events are retained as high-value evidence. Downstream engines
            // still decide what the user needs to know and what Sentinel can do about it.
            return record.Level == 1;
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
            string LatestEventMessage,
            bool CollectionAvailable = true)
        {
            public static EventLogStatusSnapshot Unavailable { get; } =
                new(0, 0, null, "Unavailable", "Windows critical/error event evidence could not be collected.", false);
        }
    }
}
