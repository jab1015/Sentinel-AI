/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Collects common Windows startup persistence entries without modifying the system.
    /// Findings are evidence only; the Investigation Engine decides whether attention is required.
    /// </summary>
    public sealed class StartupPersistenceMonitor
    {
        private static readonly string[] RunKeyPaths =
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
        };

        public StartupPersistenceSnapshot GetSnapshot()
        {
            List<StartupEntry> entries = new();

            ReadRegistryHive(Registry.CurrentUser, "Current user", entries);
            ReadRegistryHive(Registry.LocalMachine, "All users", entries);
            ReadStartupFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "Current user startup folder",
                entries);
            ReadStartupFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                "All users startup folder",
                entries);

            StartupEntry? primary = null;
            int reviewCount = 0;

            foreach (StartupEntry entry in entries)
            {
                if (!entry.RequiresReview)
                {
                    continue;
                }

                reviewCount++;
                primary ??= entry;
            }

            return new StartupPersistenceSnapshot(
                entries.Count,
                reviewCount,
                primary?.Name ?? "None",
                primary?.Reason ?? "No unusual startup persistence entries were detected.");
        }

        private static void ReadRegistryHive(
            RegistryKey hive,
            string scope,
            ICollection<StartupEntry> entries)
        {
            foreach (string keyPath in RunKeyPaths)
            {
                try
                {
                    using RegistryKey? key = hive.OpenSubKey(keyPath, writable: false);
                    if (key is null)
                    {
                        continue;
                    }

                    foreach (string valueName in key.GetValueNames())
                    {
                        string command = key.GetValue(valueName)?.ToString() ?? string.Empty;
                        entries.Add(AssessEntry(
                            string.IsNullOrWhiteSpace(valueName) ? "Unnamed startup entry" : valueName,
                            command,
                            $"{scope} registry"));
                    }
                }
                catch
                {
                    // Inaccessible registry locations are skipped safely.
                }
            }
        }

        private static void ReadStartupFolder(
            string folderPath,
            string source,
            ICollection<StartupEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(folderPath))
                {
                    entries.Add(AssessEntry(Path.GetFileName(file), file, source));
                }
            }
            catch
            {
                // Inaccessible startup folders are skipped safely.
            }
        }

        private static StartupEntry AssessEntry(string name, string command, string source)
        {
            string normalized = command.Trim().Trim('"');
            bool temporaryLocation = ContainsPathSegment(normalized, @"\Temp\");
            bool downloadsLocation = ContainsPathSegment(normalized, @"\Downloads\");
            bool scriptHost =
                normalized.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("pwsh", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("wscript", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("cscript", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("mshta", StringComparison.OrdinalIgnoreCase);

            if (temporaryLocation)
            {
                return new StartupEntry(
                    name,
                    command,
                    source,
                    true,
                    $"Starts automatically from a temporary folder ({source}).");
            }

            if (downloadsLocation)
            {
                return new StartupEntry(
                    name,
                    command,
                    source,
                    true,
                    $"Starts automatically from the Downloads folder ({source}).");
            }

            if (scriptHost)
            {
                return new StartupEntry(
                    name,
                    command,
                    source,
                    true,
                    $"Uses a script or living-off-the-land host at startup ({source}).");
            }

            return new StartupEntry(
                name,
                command,
                source,
                false,
                "Normal startup entry.");
        }

        private static bool ContainsPathSegment(string value, string segment) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Replace('/', '\\').Contains(segment, StringComparison.OrdinalIgnoreCase);

        private sealed record StartupEntry(
            string Name,
            string Command,
            string Source,
            bool RequiresReview,
            string Reason);

        public sealed record StartupPersistenceSnapshot(
            int TotalEntryCount,
            int ReviewEntryCount,
            string PrimaryEntryName,
            string PrimaryReason);
    }
}
