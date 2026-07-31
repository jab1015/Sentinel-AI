/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Reviews high-risk Windows registry persistence locations.
    /// Findings are investigation evidence and must be correlated before alerting.
    /// </summary>
    public sealed class RegistryPersistenceMonitor
    {
        public RegistryPersistenceSnapshot GetSnapshot()
        {
            List<RegistryFinding> findings = new();
            int reviewedLocations = 0;

            ReviewRunKeys(findings, ref reviewedLocations);
            ReviewWinlogon(findings, ref reviewedLocations);
            ReviewAppInitDlls(findings, ref reviewedLocations);
            ReviewImageFileExecutionOptions(findings, ref reviewedLocations);

            RegistryFinding? primary = findings.Count > 0 ? findings[0] : null;
            return new RegistryPersistenceSnapshot(
                reviewedLocations,
                findings.Count,
                primary?.Location ?? "None",
                primary?.ValueName ?? "None",
                primary?.Reason ?? "No unusual registry persistence settings were detected.");
        }

        private static void ReviewRunKeys(List<RegistryFinding> findings, ref int reviewedLocations)
        {
            RegistryLocation[] locations =
            {
                new(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU"),
                new(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU"),
                new(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
                new(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM"),
                new(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
                new(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM")
            };

            foreach (RegistryLocation location in locations)
            {
                reviewedLocations++;

                try
                {
                    using RegistryKey? key = location.Root.OpenSubKey(location.Path);
                    if (key is null)
                    {
                        continue;
                    }

                    foreach (string valueName in key.GetValueNames())
                    {
                        string command = Convert.ToString(key.GetValue(valueName))?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(command))
                        {
                            continue;
                        }

                        string expanded = Environment.ExpandEnvironmentVariables(command);
                        string executablePath = ExtractExecutablePath(expanded);
                        string reason = EvaluateRunCommand(executablePath, expanded);
                        if (string.IsNullOrWhiteSpace(reason))
                        {
                            continue;
                        }

                        findings.Add(new RegistryFinding(
                            $@"{location.HiveLabel}\{location.Path}",
                            string.IsNullOrWhiteSpace(valueName) ? "(Default)" : valueName,
                            $"{reason} Command: {Shorten(expanded)}"));
                    }
                }
                catch
                {
                    // Missing permissions or unavailable registry data are skipped safely.
                }
            }
        }

        private static string EvaluateRunCommand(string executablePath, string command)
        {
            if (IsTemporaryLocation(executablePath))
            {
                return "The startup command launches from a temporary directory.";
            }

            if (IsDownloadsLocation(executablePath))
            {
                return "The startup command launches from the Downloads directory.";
            }

            bool scriptHost =
                Contains(command, "powershell") || Contains(command, "pwsh") ||
                Contains(command, "wscript") || Contains(command, "cscript") ||
                Contains(command, "mshta");

            bool remoteContent =
                Contains(command, "http://") || Contains(command, "https://") ||
                Contains(command, "downloadstring") || Contains(command, "invoke-webrequest");

            bool encodedPowerShell =
                (Contains(command, "powershell") || Contains(command, "pwsh")) &&
                (Contains(command, " -enc ") || Contains(command, " -encodedcommand "));

            if (encodedPowerShell)
            {
                return "The startup command uses encoded PowerShell content.";
            }

            if (scriptHost && remoteContent)
            {
                return "The startup command combines a script host with remote content.";
            }

            return string.Empty;
        }

        private static void ReviewWinlogon(List<RegistryFinding> findings, ref int reviewedLocations)
        {
            const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
            reviewedLocations++;

            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
                if (key is null)
                {
                    return;
                }

                string shell = Convert.ToString(key.GetValue("Shell"))?.Trim() ?? string.Empty;
                string userinit = Convert.ToString(key.GetValue("Userinit"))?.Trim() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(shell) &&
                    !shell.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new RegistryFinding(
                        $@"HKLM\{path}",
                        "Shell",
                        $"Winlogon Shell differs from the Windows default: {Shorten(shell)}"));
                }

                if (!string.IsNullOrWhiteSpace(userinit) &&
                    !IsExpectedUserinit(userinit))
                {
                    findings.Add(new RegistryFinding(
                        $@"HKLM\{path}",
                        "Userinit",
                        $"Winlogon Userinit contains an unexpected command: {Shorten(userinit)}"));
                }
            }
            catch
            {
                // Unavailable registry data is skipped safely.
            }
        }

        private static void ReviewAppInitDlls(List<RegistryFinding> findings, ref int reviewedLocations)
        {
            string[] paths =
            {
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Windows"
            };

            foreach (string path in paths)
            {
                reviewedLocations++;

                try
                {
                    using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
                    if (key is null)
                    {
                        continue;
                    }

                    string appInitDlls = Convert.ToString(key.GetValue("AppInit_DLLs"))?.Trim() ?? string.Empty;
                    int loadAppInitDlls = ConvertToInt32(key.GetValue("LoadAppInit_DLLs"));

                    if (loadAppInitDlls != 0 && !string.IsNullOrWhiteSpace(appInitDlls))
                    {
                        findings.Add(new RegistryFinding(
                            $@"HKLM\{path}",
                            "AppInit_DLLs",
                            $"Windows is configured to load additional DLLs into user processes: {Shorten(appInitDlls)}"));
                    }
                }
                catch
                {
                    // Unavailable registry data is skipped safely.
                }
            }
        }

        private static void ReviewImageFileExecutionOptions(
            List<RegistryFinding> findings,
            ref int reviewedLocations)
        {
            const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
            reviewedLocations++;

            try
            {
                using RegistryKey? root = Registry.LocalMachine.OpenSubKey(path);
                if (root is null)
                {
                    return;
                }

                foreach (string subKeyName in root.GetSubKeyNames())
                {
                    try
                    {
                        using RegistryKey? subKey = root.OpenSubKey(subKeyName);
                        string debugger = Convert.ToString(subKey?.GetValue("Debugger"))?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(debugger))
                        {
                            continue;
                        }

                        findings.Add(new RegistryFinding(
                            $@"HKLM\{path}\{subKeyName}",
                            "Debugger",
                            $"A debugger command is configured to intercept {subKeyName}: {Shorten(debugger)}"));
                    }
                    catch
                    {
                        // Individual inaccessible entries are skipped safely.
                    }
                }
            }
            catch
            {
                // Unavailable registry data is skipped safely.
            }
        }

        private static string ExtractExecutablePath(string command)
        {
            string value = command.Trim();
            if (value.StartsWith('"'))
            {
                int closingQuote = value.IndexOf('"', 1);
                return closingQuote > 1 ? value[1..closingQuote] : value.Trim('"');
            }

            int firstSpace = value.IndexOf(' ');
            return firstSpace > 0 ? value[..firstSpace] : value;
        }

        private static bool IsTemporaryLocation(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string temp = Path.GetFullPath(Path.GetTempPath());
                return fullPath.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDownloadsLocation(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string downloads = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");
                return fullPath.StartsWith(downloads, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsExpectedUserinit(string value)
        {
            string normalized = value.Replace('/', '\\').Trim().TrimEnd(',');
            return normalized.EndsWith(@"\System32\userinit.exe", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("userinit.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static int ConvertToInt32(object? value)
        {
            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static bool Contains(string value, string text) =>
            value.Contains(text, StringComparison.OrdinalIgnoreCase);

        private static string Shorten(string value) =>
            value.Length <= 160 ? value : value[..157] + "...";

        private sealed record RegistryLocation(RegistryKey Root, string Path, string HiveLabel);
        private sealed record RegistryFinding(string Location, string ValueName, string Reason);

        public sealed record RegistryPersistenceSnapshot(
            int ReviewedLocationCount,
            int ReviewFindingCount,
            string PrimaryLocation,
            string PrimaryValueName,
            string PrimaryReason);
    }
}
