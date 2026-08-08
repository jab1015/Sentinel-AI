/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Performs bounded, read-only analysis of a local Windows crash dump when the
    /// Microsoft debugger is already installed. Sentinel never installs a debugger,
    /// enables Driver Verifier, changes dump settings, or uploads dump contents.
    /// </summary>
    public sealed class CrashDumpAnalysisService
    {
        private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromSeconds(45);

        public CrashDumpAnalysisResult AnalyzeLatest(DateTime? incidentTime)
        {
            string? dumpPath = FindLatestDump(incidentTime);
            if (dumpPath is null)
                return CrashDumpAnalysisResult.NoDump();

            string? debugger = FindDebugger();
            if (debugger is null)
                return CrashDumpAnalysisResult.DebuggerUnavailable(dumpPath);

            try
            {
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = debugger,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.StartInfo.ArgumentList.Add("-z");
                process.StartInfo.ArgumentList.Add(dumpPath);
                process.StartInfo.ArgumentList.Add("-c");
                process.StartInfo.ArgumentList.Add("!analyze -v; q");

                if (!process.Start())
                    return CrashDumpAnalysisResult.Failed(dumpPath, "The Microsoft debugger could not be started.");

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit((int)AnalysisTimeout.TotalMilliseconds))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return CrashDumpAnalysisResult.Failed(dumpPath, "Local crash-dump analysis exceeded the safety timeout.");
                }

                string combined = output + Environment.NewLine + error;
                string image = Extract(combined, @"(?im)^\s*IMAGE_NAME\s*:\s*(\S+)");
                string module = Extract(combined, @"(?im)^\s*MODULE_NAME\s*:\s*(\S+)");
                string probable = Extract(combined, @"(?im)^\s*Probably caused by\s*:\s*([^\r\n]+)");
                string bucket = Extract(combined, @"(?im)^\s*FAILURE_BUCKET_ID\s*:\s*([^\r\n]+)");
                string processName = Extract(combined, @"(?im)^\s*PROCESS_NAME\s*:\s*(\S+)");

                string candidate = FirstSpecificDriver(image, module, probable);
                bool identified = !string.IsNullOrWhiteSpace(candidate);
                string summary = identified
                    ? $"Microsoft crash-dump analysis identified {candidate} as the primary faulting-module candidate. This is crash-specific evidence, but Sentinel should correlate the module with the installed driver and a second crash before calling the root cause verified."
                    : "Microsoft crash-dump analysis completed but did not identify a specific third-party driver. Sentinel will not infer one from the stop code alone.";

                return new CrashDumpAnalysisResult(
                    true, true, identified, dumpPath, candidate, processName, bucket,
                    identified ? 80 : 0, summary);
            }
            catch (UnauthorizedAccessException)
            {
                return CrashDumpAnalysisResult.Failed(dumpPath, "Windows denied read access to the crash dump.");
            }
            catch (Exception ex)
            {
                return CrashDumpAnalysisResult.Failed(dumpPath, $"Local crash-dump analysis stopped safely ({ex.GetType().Name}).");
            }
        }

        private static string? FindLatestDump(DateTime? incidentTime)
        {
            try
            {
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string minidump = Path.Combine(windows, "Minidump");
                var candidates = Directory.Exists(minidump)
                    ? new DirectoryInfo(minidump).EnumerateFiles("*.dmp", SearchOption.TopDirectoryOnly)
                    : Enumerable.Empty<FileInfo>();

                string memoryDump = Path.Combine(windows, "MEMORY.DMP");
                if (File.Exists(memoryDump))
                    candidates = candidates.Append(new FileInfo(memoryDump));

                DateTime earliest = (incidentTime ?? DateTime.Now).AddHours(-2);
                DateTime latest = (incidentTime ?? DateTime.Now).AddHours(2);
                return candidates
                    .Where(file => file.LastWriteTime >= earliest && file.LastWriteTime <= latest)
                    .OrderByDescending(file => file.LastWriteTime)
                    .Select(file => file.FullName)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private static string? FindDebugger()
        {
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] candidates =
            {
                Path.Combine(programFilesX86, "Windows Kits", "10", "Debuggers", "x64", "cdb.exe"),
                Path.Combine(programFiles, "Windows Kits", "10", "Debuggers", "x64", "cdb.exe"),
                Path.Combine(programFilesX86, "Windows Kits", "10", "Debuggers", "x86", "cdb.exe")
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        private static string Extract(string value, string pattern)
        {
            Match match = Regex.Match(value ?? string.Empty, pattern);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private static string FirstSpecificDriver(params string[] values)
        {
            foreach (string raw in values)
            {
                string value = raw.Trim();
                Match sys = Regex.Match(value, @"(?i)\b([a-z0-9_.-]+\.sys)\b");
                if (sys.Success && !IsGeneric(sys.Groups[1].Value))
                    return sys.Groups[1].Value;

                if (!string.IsNullOrWhiteSpace(value) &&
                    value.IndexOf(' ') < 0 &&
                    !IsGeneric(value))
                    return value;
            }
            return string.Empty;
        }

        private static bool IsGeneric(string value) =>
            value.Equals("ntoskrnl.exe", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("ntkrnlmp.exe", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("memory_corruption", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("hardware", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("unknown", StringComparison.OrdinalIgnoreCase);

        public sealed record CrashDumpAnalysisResult(
            bool DumpFound,
            bool AnalysisCompleted,
            bool CandidateIdentified,
            string DumpPath,
            string CandidateModule,
            string ProcessName,
            string FailureBucketId,
            int ConfidencePercent,
            string Summary)
        {
            public static CrashDumpAnalysisResult NoDump() =>
                new(false, false, false, string.Empty, string.Empty, string.Empty, string.Empty, 0,
                    "No crash dump matching the incident time was found.");
            public static CrashDumpAnalysisResult DebuggerUnavailable(string path) =>
                new(true, false, false, path, string.Empty, string.Empty, string.Empty, 0,
                    "A matching crash dump is present, but Microsoft Debugging Tools for Windows is not installed. Sentinel did not upload or modify the dump.");
            public static CrashDumpAnalysisResult Failed(string path, string summary) =>
                new(true, false, false, path, string.Empty, string.Empty, string.Empty, 0, summary);
        }
    }
}
