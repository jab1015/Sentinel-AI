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
        private const int MaximumEvents = 12;

        public string GetStartupLogonEvidence(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            BootEvidence evidence = ReadRecentBootEvidence();
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
                {
                    string degradations = string.Join("; ", evidence.DegradationEvents.Take(5));
                    observations.Add($"Windows also recorded startup degradation evidence: {degradations}");
                }
            }

            if (snapshot.StartupPersistenceMonitoringAvailable)
            {
                observations.Add(snapshot.FlaggedStartupEntryCount > 0
                    ? $"Sentinel checked {snapshot.StartupEntryCount} startup entries and flagged {snapshot.FlaggedStartupEntryCount}; the primary flagged entry is {snapshot.PrimaryFlaggedStartupEntryName}: {snapshot.PrimaryFlaggedStartupEntryReason}"
                    : $"Sentinel checked {snapshot.StartupEntryCount} startup entries and did not flag an unusual startup-persistence entry");
            }
            else
            {
                observations.Add("Sentinel could not completely collect startup-entry evidence during this check");
            }

            if (snapshot.ServiceMonitoringAvailable)
                observations.Add($"{snapshot.RunningServiceCount} of {snapshot.InstalledServiceCount} Windows services are currently running, with {snapshot.FlaggedServiceCount} flagged service condition(s)");

            if (snapshot.ProcessMonitoringAvailable)
                observations.Add($"the current session has {snapshot.ProcessCount} running processes; the highest-memory process is {snapshot.HighestMemoryProcessName} at {snapshot.HighestMemoryProcessGB:0.00} GB");

            string currentResources = $"Current readings after sign-in are CPU {snapshot.CpuUsagePercent:0.0}%, memory {snapshot.MemoryUsagePercent:0.0}%, and disk {snapshot.DiskUsagePercent:0.0}% used.";

            if (!evidence.Available)
            {
                return "I treated this as a local startup/sign-in performance question and checked the evidence available on this computer first. " +
                       "Windows' Diagnostics-Performance log could not be read during this check, so I cannot verify the measured boot or post-boot duration. " +
                       string.Join(" ", observations) + ". " + currentResources + " " +
                       "That local evidence is not enough to name a root cause safely. I can next check additional Windows logs or approved external guidance, but I will not guess.";
            }

            if (observations.Count == 0)
            {
                return "I checked this computer's local Windows startup evidence first, but the recent Diagnostics-Performance records did not contain enough usable timing data to explain the delay. " +
                       currentResources + " I do not have enough verified local evidence to name a cause yet, so I will not guess.";
            }

            string reason = BuildReason(evidence, snapshot);
            return "I checked this computer's local startup evidence before looking outward. " +
                   string.Join(" ", observations) + ". " + currentResources + " " + reason +
                   " These records can identify measured delays and likely contributors, but they do not prove a single root cause unless Windows recorded a specific degradation source.";
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

        private static BootEvidence ReadRecentBootEvidence()
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

                while (read < MaximumEvents)
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
