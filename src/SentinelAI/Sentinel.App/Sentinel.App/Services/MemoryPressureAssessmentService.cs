/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Read-only memory-pressure assessment. Sentinel measures physical memory
    /// pressure and identifies unusually large user processes, but this service
    /// never terminates, suspends, trims, or reconfigures a process.
    /// </summary>
    public sealed class MemoryPressureAssessmentService
    {
        private const long LargeProcessThresholdBytes = 500L * 1024 * 1024;

        public MemoryPressureAssessment Assess()
        {
            MEMORYSTATUSEX memory = new()
            {
                dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
            };

            if (!GlobalMemoryStatusEx(ref memory))
            {
                return new MemoryPressureAssessment(
                    false,
                    0,
                    0,
                    0,
                    Array.Empty<MemoryProcessEvidence>(),
                    false,
                    "Sentinel could not verify system memory pressure. No automatic memory action is warranted.");
            }

            ulong usedBytes = memory.ullTotalPhys >= memory.ullAvailPhys
                ? memory.ullTotalPhys - memory.ullAvailPhys
                : 0;

            double usedPercent = memory.ullTotalPhys > 0
                ? (usedBytes * 100d) / memory.ullTotalPhys
                : 0d;

            var processes = new List<MemoryProcessEvidence>();

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    long workingSet = Math.Max(process.WorkingSet64, 0);
                    if (workingSet < LargeProcessThresholdBytes)
                        continue;

                    string processName = process.ProcessName;
                    bool isSentinel = processName.Contains("Sentinel", StringComparison.OrdinalIgnoreCase);
                    bool isSystemLike = IsSystemProcess(processName);

                    processes.Add(new MemoryProcessEvidence(
                        process.Id,
                        processName,
                        workingSet,
                        isSentinel,
                        isSystemLike));
                }
                catch
                {
                    // Protected or exited processes are ignored; inaccessible data is
                    // never treated as evidence for an automatic action.
                }
                finally
                {
                    process.Dispose();
                }
            }

            MemoryProcessEvidence[] largest = processes
                .OrderByDescending(item => item.WorkingSetBytes)
                .Take(10)
                .ToArray();

            bool sustainedInvestigationWarranted =
                usedPercent >= 90d &&
                memory.ullAvailPhys <= 2UL * 1024 * 1024 * 1024;

            string summary = sustainedInvestigationWarranted
                ? $"System memory pressure is high at {usedPercent:0}% used with {memory.ullAvailPhys / (1024d * 1024d * 1024d):0.0} GB available. Sentinel should verify persistence and process behavior before considering remediation."
                : $"System memory pressure does not currently justify remediation. Memory use is {usedPercent:0}% with {memory.ullAvailPhys / (1024d * 1024d * 1024d):0.0} GB available.";

            return new MemoryPressureAssessment(
                true,
                memory.ullTotalPhys,
                memory.ullAvailPhys,
                usedPercent,
                largest,
                sustainedInvestigationWarranted,
                summary);
        }

        private static bool IsSystemProcess(string name)
        {
            string[] protectedNames =
            {
                "System", "Idle", "Registry", "Memory Compression", "dwm", "winlogon",
                "csrss", "smss", "services", "lsass", "svchost", "fontdrvhost",
                "audiodg", "spoolsv", "SearchIndexer"
            };

            return protectedNames.Any(item =>
                name.Equals(item, StringComparison.OrdinalIgnoreCase));
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }

    public sealed record MemoryPressureAssessment(
        bool MemoryStatusVerified,
        ulong TotalPhysicalBytes,
        ulong AvailablePhysicalBytes,
        double UsedPercent,
        IReadOnlyList<MemoryProcessEvidence> LargestProcesses,
        bool RemediationInvestigationWarranted,
        string Summary);

    public sealed record MemoryProcessEvidence(
        int ProcessId,
        string ProcessName,
        long WorkingSetBytes,
        bool IsSentinel,
        bool IsSystemLike);
}
