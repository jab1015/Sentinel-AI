/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
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

        private static string Shorten(string value) =>
            value.Length <= 160 ? value : value[..157] + "...";

        private sealed record RegistryFinding(string Location, string ValueName, string Reason);

        public sealed record RegistryPersistenceSnapshot(
            int ReviewedLocationCount,
            int ReviewFindingCount,
            string PrimaryLocation,
            string PrimaryValueName,
            string PrimaryReason);
    }
}
