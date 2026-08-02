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
            string latestEventMessage = "No actionable critical or error events detected in the last 24 hours.";

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

                    string description = GetSafeDescription(record);
                    if (!IsActionable(record, description))
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

        private static bool IsActionable(EventRecord record, string description)
        {
            if (record.Level == 1)
            {
                return true;
            }

            string provider = record.ProviderName ?? string.Empty;

            if (provider.Contains("DistributedCOM", StringComparison.OrdinalIgnoreCase) &&
                (record.Id == 10010 || record.Id == 10016))
            {
                return false;
            }

            if (provider.Contains("CertificateServicesClient-CertEnroll", StringComparison.OrdinalIgnoreCase) &&
                description.Contains("SCEP Certificate enrollment initialization", StringComparison.OrdinalIgnoreCase) &&
                description.Contains("microsoftaik.azure.net", StringComparison.OrdinalIgnoreCase) &&
                description.Contains("GetCACaps", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (provider.Contains("Security-SPP", StringComparison.OrdinalIgnoreCase) &&
                description.Contains("License Activation", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (provider.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase))
            {
                // Bluetooth dependency failures on machines with Bluetooth disabled or no
                // Bluetooth hardware are expected and do not require user action.
                if (description.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) &&
                    description.Contains("disabled or because it has no enabled devices associated with it", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Camera frame-server failures are not security findings. They matter only when
                // the user is actually experiencing a camera problem, so Sentinel keeps them
                // quiet rather than presenting them as a computer-security warning.
                if (description.Contains("Windows Camera Frame Server Monitor", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // NcbService can report a missing/virtual device condition during normal VM and
                // device-state transitions. By itself this does not establish a security or
                // reliability problem requiring user action.
                if (description.Contains("NcbService", StringComparison.OrdinalIgnoreCase) &&
                    description.Contains("A device attached to the system is not functioning", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return ContainsAny(
                    description,
                    "terminated unexpectedly",
                    "failed to start",
                    "could not be started",
                    "service hung on starting",
                    "failed to launch");
            }

            if (provider.Contains("WindowsUpdateClient", StringComparison.OrdinalIgnoreCase) &&
                description.Contains("0x80073D02", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (description.Contains("Microsoft Storage Spaces SMP", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static bool ContainsAny(string value, params string[] phrases)
        {
            foreach (string phrase in phrases)
            {
                if (value.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
