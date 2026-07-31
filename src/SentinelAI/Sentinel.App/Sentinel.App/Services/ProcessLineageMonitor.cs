/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Collects parent-child process relationships as investigation evidence.
    /// A lineage finding is not actionable by itself and must be correlated with
    /// other process, persistence, or network evidence.
    /// </summary>
    public sealed class ProcessLineageMonitor
    {
        private static readonly HashSet<string> ScriptAndProxyProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "powershell", "pwsh", "cmd", "wscript", "cscript", "mshta",
            "rundll32", "regsvr32", "certutil", "bitsadmin"
        };

        private static readonly HashSet<string> DocumentAndBrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "winword", "excel", "powerpnt", "outlook", "acrord32", "msedge",
            "chrome", "firefox", "brave", "opera"
        };

        public ProcessLineageSnapshot GetSnapshot()
        {
            Dictionary<uint, ProcessEntry> processes = EnumerateProcesses();
            List<LineageFinding> findings = new();
            int relationshipCount = 0;

            foreach (ProcessEntry child in processes.Values)
            {
                if (child.ParentProcessId == 0 ||
                    !processes.TryGetValue(child.ParentProcessId, out ProcessEntry parent))
                {
                    continue;
                }

                relationshipCount++;

                if (DocumentAndBrowserProcesses.Contains(parent.Name) &&
                    ScriptAndProxyProcesses.Contains(child.Name))
                {
                    findings.Add(new LineageFinding(
                        child.Name,
                        parent.Name,
                        $"{parent.Name} started {child.Name}. This relationship is unusual and is being correlated with other evidence."));
                }
            }

            LineageFinding? primary = findings.Count > 0 ? findings[0] : null;
            return new ProcessLineageSnapshot(
                relationshipCount,
                findings.Count,
                primary?.ChildProcessName ?? "None",
                primary?.ParentProcessName ?? "None",
                primary?.Reason ?? "No unusual parent-child process relationships were detected.");
        }

        private static Dictionary<uint, ProcessEntry> EnumerateProcesses()
        {
            Dictionary<uint, ProcessEntry> processes = new();
            IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
            if (snapshot == InvalidHandleValue)
            {
                return processes;
            }

            try
            {
                ProcessEntry32 entry = new()
                {
                    Size = (uint)Marshal.SizeOf<ProcessEntry32>()
                };

                if (!Process32First(snapshot, ref entry))
                {
                    return processes;
                }

                do
                {
                    string name = NormalizeProcessName(entry.ExecutableFile);
                    processes[entry.ProcessId] = new ProcessEntry(
                        entry.ProcessId,
                        entry.ParentProcessId,
                        name);

                    entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
                }
                while (Process32Next(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return processes;
        }

        private static string NormalizeProcessName(string value)
        {
            string name = value?.Trim() ?? string.Empty;
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;
        }

        private const uint Th32csSnapProcess = 0x00000002;
        private static readonly IntPtr InvalidHandleValue = new(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry32
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint Threads;
            public uint ParentProcessId;
            public int PriorityClassBase;
            public uint Flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExecutableFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        private sealed record ProcessEntry(uint ProcessId, uint ParentProcessId, string Name);
        private sealed record LineageFinding(string ChildProcessName, string ParentProcessName, string Reason);

        public sealed record ProcessLineageSnapshot(
            int RelationshipCount,
            int ReviewRelationshipCount,
            string PrimaryChildProcessName,
            string PrimaryParentProcessName,
            string PrimaryReason);
    }
}
