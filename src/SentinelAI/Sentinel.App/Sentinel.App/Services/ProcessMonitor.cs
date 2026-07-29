/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Sentinel.App.Services
{
    public class ProcessMonitor
    {
        public ProcessIntelligenceSnapshot GetIntelligence()
        {
            List<ProcessFinding> findings = new();
            Process[] processes = Process.GetProcesses();
            string highestMemoryProcessName = "Unknown";
            long highestWorkingSet = 0;

            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        string processName = process.ProcessName;
                        long workingSet = process.WorkingSet64;

                        if (workingSet > highestWorkingSet)
                        {
                            highestWorkingSet = workingSet;
                            highestMemoryProcessName = processName;
                        }

                        if (workingSet >= 2L * 1024 * 1024 * 1024)
                        {
                            findings.Add(new ProcessFinding(
                                processName,
                                $"High memory use ({workingSet / 1024d / 1024d / 1024d:0.00} GB)"));
                        }

                        // File-path inspection is intentionally limited to processes that
                        // can be queried safely. Protected and exited processes are skipped.
                        string path = GetProcessPath(process);
                        if (IsUserWritableLocation(path))
                        {
                            findings.Add(new ProcessFinding(
                                processName,
                                $"Running from a user-writable location: {ShortenPath(path)}"));
                        }
                    }
                    catch
                    {
                        // Protected and exited processes are skipped safely.
                    }
                }

                ProcessFinding? primary = findings.Count > 0 ? findings[0] : null;
                return new ProcessIntelligenceSnapshot(
                    processes.Length,
                    highestMemoryProcessName,
                    Math.Round(highestWorkingSet / 1024d / 1024d / 1024d, 2),
                    findings.Count,
                    primary?.ProcessName ?? "None",
                    primary?.Reason ?? "No process warning conditions were detected.");
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }

        private static string GetProcessPath(Process process)
        {
            try { return process.MainModule?.FileName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool IsUserWritableLocation(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                string temp = Path.GetFullPath(Path.GetTempPath());
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloads = Path.Combine(userProfile, "Downloads");
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                return fullPath.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
                       fullPath.StartsWith(downloads, StringComparison.OrdinalIgnoreCase) ||
                       fullPath.StartsWith(appData, StringComparison.OrdinalIgnoreCase) ||
                       fullPath.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ShortenPath(string path) =>
            path.Length <= 90 ? path : "..." + path[^87..];

        private sealed record ProcessFinding(string ProcessName, string Reason);

        public sealed record ProcessIntelligenceSnapshot(
            int TotalProcessCount,
            string HighestMemoryProcessName,
            double HighestMemoryProcessGB,
            int FlaggedProcessCount,
            string PrimaryProcessName,
            string PrimaryReason);
    }
}
