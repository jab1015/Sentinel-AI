/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Sentinel.App.Services
{
    public class ProcessMonitor
    {
        public int GetProcessCount() => Process.GetProcesses().Length;

        public int GetRunningServiceCount()
        {
            return Process.GetProcesses().Count(process =>
            {
                try { return process.SessionId == 0; }
                catch { return false; }
                finally { process.Dispose(); }
            });
        }

        public ProcessIntelligenceSnapshot GetIntelligence()
        {
            List<ProcessFinding> findings = new();
            Process[] processes = Process.GetProcesses();

            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        string processName = process.ProcessName;
                        long workingSet = process.WorkingSet64;
                        string path = GetProcessPath(process);

                        if (workingSet >= 2L * 1024 * 1024 * 1024)
                        {
                            findings.Add(new ProcessFinding(
                                processName,
                                $"High memory use ({workingSet / 1024d / 1024d / 1024d:0.00} GB)"));
                        }

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

                ProcessFinding? primary = findings.FirstOrDefault();
                return new ProcessIntelligenceSnapshot(
                    processes.Length,
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

        public string GetHighestMemoryProcess() => GetHighestMemoryProcessSnapshot().Name;

        public double GetHighestMemoryProcessGB() => GetHighestMemoryProcessSnapshot().MemoryGB;

        private static (string Name, double MemoryGB) GetHighestMemoryProcessSnapshot()
        {
            Process[] processes = Process.GetProcesses();
            try
            {
                Process? highest = processes
                    .OrderByDescending(process =>
                    {
                        try { return process.WorkingSet64; }
                        catch { return 0L; }
                    })
                    .FirstOrDefault();

                if (highest is null)
                {
                    return ("Unknown", 0);
                }

                return (
                    highest.ProcessName,
                    Math.Round(highest.WorkingSet64 / 1024d / 1024d / 1024d, 2));
            }
            catch
            {
                return ("Unknown", 0);
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

        private static string ShortenPath(string path)
        {
            return path.Length <= 90 ? path : "..." + path[^87..];
        }

        private sealed record ProcessFinding(string ProcessName, string Reason);

        public sealed record ProcessIntelligenceSnapshot(
            int TotalProcessCount,
            int FlaggedProcessCount,
            string PrimaryProcessName,
            string PrimaryReason);
    }
}
