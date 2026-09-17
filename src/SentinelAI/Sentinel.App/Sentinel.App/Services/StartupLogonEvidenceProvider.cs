/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Reads bounded local Windows boot/startup evidence for Ask Sentinel. This provider
    /// never reaches the network and never treats a generic active finding as the cause
    /// of a slow sign-in.
    /// </summary>
    public sealed class StartupLogonEvidenceProvider
    {
        private const string DiagnosticsPerformanceLog = "Microsoft-Windows-Diagnostics-Performance/Operational";
        private const string UserProfileLog = "Microsoft-Windows-User Profiles Service/Operational";
        private const int MaximumEvents = 12;
        private const int MaximumDeepEvents = 40;

        public string GetStartupLogonEvidence(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            BootEvidence evidence = ReadRecentBootEvidence(MaximumEvents);
            List<string> observations = BuildSnapshotObservations(snapshot, evidence);
            string currentResources = $"Current readings after sign-in are CPU {snapshot.CpuUsagePercent:0.0}%, memory {snapshot.MemoryUsagePercent:0.0}%, and disk {snapshot.DiskUsagePercent:0.0}% used.";

            if (!evidence.Available)
            {
                return "I treated this as a local startup/sign-in performance question and checked the evidence available on this computer first. " +
                       "Windows' Diagnostics-Performance log could not be read during this check, so I cannot verify the measured boot or post-boot duration. " +
                       string.Join(" ", observations) + ". " + currentResources + " " +
                       "The reason I cannot name one root cause is that the Windows boot-timing log was unavailable, not that Sentinel skipped the local investigation. " +
                       "This is still a completed local diagnostic result. Sentinel can run a deeper startup check next to inspect additional Windows startup, service, task, and profile evidence.";
            }

            if (observations.Count == 0)
            {
                return "I checked this computer's local Windows startup evidence first, but the recent Diagnostics-Performance records did not contain enough usable timing data to explain the delay. " +
                       currentResources + " The reason I cannot name one cause is that Windows did not record enough usable startup timing detail in the bounded local records I could verify. " +
                       "Sentinel can run a deeper startup check next rather than replacing the local findings with generic external guidance.";
            }

            string reason = BuildReason(evidence, snapshot);
            return "I checked this computer's local startup evidence before looking outward. " +
                   string.Join(" ", observations) + ". " + currentResources + " Local evidence reason: " + reason +
                   " These records can identify measured delays and likely contributors, but they do not prove a single root cause unless Windows recorded a specific degradation source. " +
                   "If you want, Sentinel can run a deeper startup check that reviews a larger Windows event window plus service, scheduled-task, and user-profile startup evidence.";
        }

        public string GetDeepStartupLogonEvidence(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            BootEvidence boot = ReadRecentBootEvidence(MaximumDeepEvents);
            IReadOnlyList<string> serviceEvents = ReadRecentEventSummaries(
                "System",
                "*[System[Provider[@Name='Service Control Manager'] and (EventID=7000 or EventID=7001 or EventID=7009 or EventID=7011 or EventID=7022 or EventID=7023 or EventID=7024 or EventID=7031 or EventID=7034) and TimeCreated[timediff(@SystemTime) <= 86400000]]]",
                8);
            IReadOnlyList<string> profileEvents = ReadRecentEventSummaries(
                UserProfileLog,
                "*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= 86400000]]]",
                8);

            List<string> findings = new();
            if (boot.Available)
            {
                if (boot.BootTimeMs.HasValue)
                    findings.Add($"Windows measured the most recent boot at {FormatDuration(boot.BootTimeMs.Value)}");
                if (boot.MainPathBootTimeMs.HasValue)
                    findings.Add($"main boot path: {FormatDuration(boot.MainPathBootTimeMs.Value)}");
                if (boot.PostBootTimeMs.HasValue)
                    findings.Add($"post-boot activity: {FormatDuration(boot.PostBootTimeMs.Value)}");
                if (boot.DegradationEvents.Count > 0)
                    findings.Add("startup degradation records: " + string.Join("; ", boot.DegradationEvents.Take(8)));
            }
            else
            {
                findings.Add("Windows Diagnostics-Performance timing data was not readable");
            }

            if (serviceEvents.Count > 0)
                findings.Add("recent service-start problems: " + string.Join("; ", serviceEvents));
            else
                findings.Add("no recent Service Control Manager startup/failure events matched the bounded check");

            if (profileEvents.Count > 0)
                findings.Add("recent user-profile warnings/errors: " + string.Join("; ", profileEvents));
            else
                findings.Add("no recent user-profile warning/error events matched the bounded check");

            if (snapshot.StartupPersistenceMonitoringAvailable)
            {
                findings.Add(snapshot.FlaggedStartupEntryCount > 0
                    ? $"startup entries: {snapshot.StartupEntryCount} checked, {snapshot.FlaggedStartupEntryCount} flagged; primary: {snapshot.PrimaryFlaggedStartupEntryName}: {snapshot.PrimaryFlaggedStartupEntryReason}"
                    : $"startup entries: {snapshot.StartupEntryCount} checked, none flagged");
            }

            if (snapshot.ScheduledTaskMonitoringAvailable)
            {
                findings.Add(snapshot.FlaggedScheduledTaskCount > 0
                    ? $"scheduled tasks: {snapshot.ScheduledTaskCount} checked, {snapshot.FlaggedScheduledTaskCount} flagged; primary: {snapshot.PrimaryFlaggedScheduledTaskName}: {snapshot.PrimaryFlaggedScheduledTaskReason}"
                    : $"scheduled tasks: {snapshot.ScheduledTaskCount} checked, none flagged");
            }

            if (snapshot.ServiceMonitoringAvailable)
                findings.Add($"services: {snapshot.RunningServiceCount} running of {snapshot.InstalledServiceCount} installed, {snapshot.FlaggedServiceCount} flagged");

            if (snapshot.ProcessMonitoringAvailable)
                findings.Add($"current session: {snapshot.ProcessCount} processes; highest memory is {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB");

            string conclusion = BuildDeepConclusion(boot, snapshot, serviceEvents.Count, profileEvents.Count);
            return "Deeper startup check completed using local Windows evidence only.\n\nWhat I checked\n" +
                   string.Join("\n", findings.Select(item => "• " + item)) +
                   "\n\nWhat this means\n" + conclusion +
                   "\n\nSentinel did not change startup apps, services, scheduled tasks, or system settings during this investigation.";
        }

        private static List<string> BuildSnapshotObservations(SystemSnapshot snapshot, BootEvidence evidence)
        {
            List<string> observations = new();
            if (evidence.Available)
            {
                if (evidence.BootTimeMs.HasValue)
                    observations.Add($"Windows measured the most recent boot at {FormatDuration(evidence.BootTimeMs.Value)}");
                if (evidence.MainPathBootTimeMs.HasValue)
                    observations.Add($"the main boot path took {FormatDuration(evidence.MainPathBootTimeMs.Value)}");
                if (evidence.PostBootTimeMs.HasValue)
                    observations.Add($"post-boot startup activity took {FormatDuration(evidence.PostBootTimeMs.Value)}");
                if (evidence.DegradationEvents.Count > 0)
                    observations.Add($"Windows also recorded startup degradation evidence: {string.Join("; ", evidence.DegradationEvents.Take(5))}");
            }

            if (snapshot.StartupPersistenceMonitoringAvailable)
                observations.Add(snapshot.FlaggedStartupEntryCount > 0
                    ? $"Sentinel checked {snapshot.StartupEntryCount} startup entries and flagged {snapshot.FlaggedStartupEntryCount}; the primary flagged entry is {snapshot.PrimaryFlaggedStartupEntryName}: {snapshot.PrimaryFlaggedStartupEntryReason}"
                    : $"Sentinel checked {snapshot.StartupEntryCount} startup entries and did not flag an unusual startup-persistence entry");
            else
                observations.Add("Sentinel could not completely collect startup-entry evidence during this check");

            if (snapshot.ServiceMonitoringAvailable)
                observations.Add($"{snapshot.RunningServiceCount} of {snapshot.InstalledServiceCount} Windows services are currently running, with {snapshot.FlaggedServiceCount} flagged service condition(s)");

            if (snapshot.ProcessMonitoringAvailable)
                observations.Add($"the current session has {snapshot.ProcessCount} running processes; the highest-memory process is {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB");

            return observations;
        }

        private static string BuildDeepConclusion(BootEvidence evidence, SystemSnapshot snapshot, int serviceEventCount, int profileEventCount)
        {
            if (evidence.DegradationEvents.Count > 0)
                return "Windows recorded specific startup degradation events, so those entries are the strongest local leads. Sentinel should investigate the named item(s) before making any broad startup change.";
            if (serviceEventCount > 0)
                return "The deeper check found recent service-start failures or timeouts. Those services are stronger local leads than unrelated running processes or generic web guidance.";
            if (profileEventCount > 0)
                return "The deeper check found user-profile warnings or errors near sign-in. Those events are worth correlating with the slow-login period before changing startup applications.";
            if (evidence.PostBootTimeMs is >= 120_000)
                return "Most of the measured delay is post-boot, which makes startup applications, scheduled tasks, and services the next local areas to narrow down.";
            if (evidence.MainPathBootTimeMs is >= 120_000)
                return "The main Windows boot path itself is unusually long, so firmware, storage, drivers, or core Windows startup deserve priority over ordinary startup apps.";
            if (snapshot.FlaggedStartupEntryCount > 0 || snapshot.FlaggedScheduledTaskCount > 0 || snapshot.FlaggedServiceCount > 0)
                return "Sentinel found one or more locally flagged startup-related items. Review those specific items first; the evidence still does not prove that any one of them caused the entire delay.";
            return "The deeper local check still does not isolate one verified cause. Sentinel has now ruled out the bounded startup, service-failure, user-profile warning, scheduled-task, and flagged-startup evidence it could safely inspect without changing the system.";
        }

        private static string BuildReason(BootEvidence evidence, SystemSnapshot snapshot)
        {
            if (evidence.DegradationEvents.Count > 0)
                return "The strongest local reason is the startup degradation event Windows recorded above.";
            if (evidence.PostBootTimeMs is >= 120_000)
                return "The local evidence shows that a large share of the delay occurred after the main boot path, which points toward post-boot startup work such as startup applications or services rather than firmware or the early Windows boot path.";
            if (evidence.MainPathBootTimeMs is >= 120_000)
                return "The local evidence shows that the main Windows boot path itself was unusually long, so the delay happened before normal post-boot startup work completed.";
            if (snapshot.FlaggedStartupEntryCount > 0)
                return "Sentinel found a flagged startup entry that is worth reviewing as a possible contributor, but the current evidence does not prove that it caused the full sign-in delay.";
            return "The local evidence does not yet isolate one verified cause for the long sign-in.";
        }

        private static BootEvidence ReadRecentBootEvidence(int maximumEvents)
        {
            try
            {
                string queryText = "*[System[(EventID=100 or (EventID>=101 and EventID<=110))]]";
                EventLogQuery query = new(DiagnosticsPerformanceLog, PathType.LogName, queryText)
                {
                    ReverseDirection = true,
                    TolerateQueryErrors = true
                };

                using EventLogReader reader = new(query);
                long? bootTime = null;
                long? mainPath = null;
                long? postBoot = null;
                List<string> degradationEvents = new();
                int read = 0;

                while (read < maximumEvents)
                {
                    using EventRecord? record = reader.ReadEvent();
                    if (record is null) break;
                    read++;

                    Dictionary<string, string> data = ReadEventData(record);
                    if (record.Id == 100 && !bootTime.HasValue)
                    {
                        bootTime = ReadMilliseconds(data, "BootTime");
                        mainPath = ReadMilliseconds(data, "MainPathBootTime");
                        postBoot = ReadMilliseconds(data, "BootPostBootTime") ?? ReadMilliseconds(data, "PostBootTime");
                        continue;
                    }

                    if (record.Id is >= 101 and <= 110)
                    {
                        string? item = FirstNonEmpty(data, "Name", "FriendlyName", "Path", "FileName", "ServiceName");
                        long? total = ReadMilliseconds(data, "TotalTime") ?? ReadMilliseconds(data, "StartTime");
                        long? degradation = ReadMilliseconds(data, "DegradationTime");
                        string label = string.IsNullOrWhiteSpace(item) ? $"event {record.Id}" : item;
                        if (degradation.HasValue)
                            label += $" (+{FormatDuration(degradation.Value)} degradation)";
                        else if (total.HasValue)
                            label += $" ({FormatDuration(total.Value)})";
                        degradationEvents.Add(label);
                    }
                }

                return new BootEvidence(true, bootTime, mainPath, postBoot, degradationEvents);
            }
            catch
            {
                return new BootEvidence(false, null, null, null, Array.Empty<string>());
            }
        }

        private static IReadOnlyList<string> ReadRecentEventSummaries(string logName, string queryText, int maximumEvents)
        {
            List<string> results = new();
            try
            {
                EventLogQuery query = new(logName, PathType.LogName, queryText)
                {
                    ReverseDirection = true,
                    TolerateQueryErrors = true
                };
                using EventLogReader reader = new(query);
                while (results.Count < maximumEvents)
                {
                    using EventRecord? record = reader.ReadEvent();
                    if (record is null) break;
                    Dictionary<string, string> data = ReadEventData(record);
                    string? detail = FirstNonEmpty(data, "ServiceName", "param1", "Name", "FileName", "ErrorCode", "Status");
                    string when = record.TimeCreated.HasValue ? record.TimeCreated.Value.ToLocalTime().ToString("MMM d h:mm tt") : "time unavailable";
                    results.Add(string.IsNullOrWhiteSpace(detail)
                        ? $"event {record.Id} at {when}"
                        : $"event {record.Id} ({detail}) at {when}");
                }
            }
            catch
            {
                // Missing/disabled event channels are treated as unavailable evidence.
            }
            return results;
        }

        private static Dictionary<string, string> ReadEventData(EventRecord record)
        {
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                XDocument document = XDocument.Parse(record.ToXml(), LoadOptions.None);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
                foreach (XElement data in document.Descendants(ns + "Data"))
                {
                    string? name = data.Attribute("Name")?.Value;
                    if (!string.IsNullOrWhiteSpace(name)) values[name] = data.Value?.Trim() ?? string.Empty;
                }
            }
            catch
            {
                // A malformed/unavailable record must not break Ask Sentinel.
            }
            return values;
        }

        private static long? ReadMilliseconds(IReadOnlyDictionary<string, string> values, string name)
        {
            if (!values.TryGetValue(name, out string? raw) || string.IsNullOrWhiteSpace(raw)) return null;
            return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) && parsed >= 0
                ? parsed
                : null;
        }

        private static string? FirstNonEmpty(IReadOnlyDictionary<string, string> values, params string[] names)
        {
            foreach (string name in names)
            {
                if (values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return null;
        }

        private static string FormatDuration(long milliseconds)
        {
            TimeSpan value = TimeSpan.FromMilliseconds(milliseconds);
            if (value.TotalMinutes >= 1) return $"{value.TotalMinutes:0.0} minutes";
            if (value.TotalSeconds >= 1) return $"{value.TotalSeconds:0.0} seconds";
            return $"{milliseconds} ms";
        }

        private sealed record BootEvidence(
            bool Available,
            long? BootTimeMs,
            long? MainPathBootTimeMs,
            long? PostBootTimeMs,
            IReadOnlyList<string> DegradationEvents);
    }
}
