/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Collects startup entries without changing them. The resulting evidence is
    /// used by the optimization engine to identify startup-load candidates safely.
    /// </summary>
    public sealed class StartupPerformanceAssessmentService
    {
        private static readonly (RegistryHive Hive, RegistryView View, string Path, string Scope)[] Locations =
        {
            (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Run", "Current user"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run", "All users"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\Run", "All users (32-bit)")
        };

        public StartupPerformanceAssessment Assess()
        {
            var entries = new List<StartupEntryEvidence>();

            foreach (var location in Locations)
            {
                try
                {
                    using RegistryKey baseKey = RegistryKey.OpenBaseKey(location.Hive, location.View);
                    using RegistryKey? key = baseKey.OpenSubKey(location.Path, writable: false);
                    if (key is null)
                        continue;

                    foreach (string name in key.GetValueNames())
                    {
                        string command = key.GetValue(name)?.ToString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(command))
                            continue;

                        entries.Add(new StartupEntryEvidence(
                            name,
                            command,
                            location.Scope,
                            IsSentinelEntry(name, command)));
                    }
                }
                catch
                {
                    // Missing/inaccessible startup locations are not treated as evidence.
                }
            }

            // Never consider Sentinel itself a speed-up candidate. Duplicates can be
            // reported as evidence, but this service performs no removal or disabling.
            int thirdPartyCount = entries.Count(entry => !entry.IsSentinel);
            bool elevatedStartupLoad = thirdPartyCount >= 10;

            string summary = elevatedStartupLoad
                ? $"Sentinel found {thirdPartyCount} non-Sentinel registry startup entries. Startup load is high enough to justify deeper impact analysis."
                : $"Sentinel found {thirdPartyCount} non-Sentinel registry startup entries. No startup change is warranted from entry count alone.";

            return new StartupPerformanceAssessment(
                entries,
                thirdPartyCount,
                elevatedStartupLoad,
                summary);
        }

        private static bool IsSentinelEntry(string name, string command) =>
            name.Contains("Sentinel", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("Sentinel.App", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("Sentinel AI", StringComparison.OrdinalIgnoreCase);
    }

    public sealed record StartupPerformanceAssessment(
        IReadOnlyList<StartupEntryEvidence> Entries,
        int ThirdPartyEntryCount,
        bool DeeperAnalysisWarranted,
        string Summary);

    public sealed record StartupEntryEvidence(
        string Name,
        string Command,
        string Scope,
        bool IsSentinel);
}
