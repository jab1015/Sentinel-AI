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
    /// <summary>
    /// Collects scheduled-task persistence evidence without changing the system.
    /// Only tasks with higher-risk execution patterns are marked for investigation.
    /// </summary>
    public sealed class ScheduledTaskMonitor
    {
        public ScheduledTaskSnapshot GetSnapshot()
        {
            List<ScheduledTaskFinding> findings = new();
            int totalTasks = 0;

            try
            {
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = "/Query /FO CSV /V",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10000);

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    return ScheduledTaskSnapshot.Unavailable;
                }

                string[] lines = output.Split(
                    new[] { "\r\n", "\n" },
                    StringSplitOptions.RemoveEmptyEntries);

                if (lines.Length <= 1)
                {
                    return new ScheduledTaskSnapshot(
                        0,
                        0,
                        "None",
                        "No scheduled tasks were returned by Windows.");
                }

                string[] headers = ParseCsvLine(lines[0]);
                int taskNameIndex = FindColumn(headers, "TaskName", "Task Name");
                int actionIndex = FindColumn(headers, "Task To Run", "Actions");
                int authorIndex = FindColumn(headers, "Author");

                for (int index = 1; index < lines.Length; index++)
                {
                    string[] values = ParseCsvLine(lines[index]);
                    if (values.Length == 0)
                    {
                        continue;
                    }

                    totalTasks++;
                    string taskName = GetValue(values, taskNameIndex, "Unknown task");
                    string action = GetValue(values, actionIndex, string.Empty);
                    string author = GetValue(values, authorIndex, "Unknown");

                    ScheduledTaskFinding? finding = Assess(taskName, action, author);
                    if (finding is not null)
                    {
                        findings.Add(finding);
                    }
                }
            }
            catch
            {
                return ScheduledTaskSnapshot.Unavailable;
            }

            ScheduledTaskFinding? primary = findings.Count > 0 ? findings[0] : null;
            return new ScheduledTaskSnapshot(
                totalTasks,
                findings.Count,
                primary?.TaskName ?? "None",
                primary?.Reason ?? "No unusual scheduled-task persistence was detected.");
        }

        private static ScheduledTaskFinding? Assess(
            string taskName,
            string action,
            string author)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return null;
            }

            string normalized = action.Replace('/', '\\');
            bool temporaryLocation =
                normalized.Contains("\\Temp\\", StringComparison.OrdinalIgnoreCase);
            bool downloadsLocation =
                normalized.Contains("\\Downloads\\", StringComparison.OrdinalIgnoreCase);
            bool scriptOrLolBin =
                normalized.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("pwsh", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("wscript", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("cscript", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("mshta", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("rundll32", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("regsvr32", StringComparison.OrdinalIgnoreCase);

            if (temporaryLocation)
            {
                return new ScheduledTaskFinding(
                    taskName,
                    $"Runs from a temporary folder. Author: {author}.");
            }

            if (downloadsLocation)
            {
                return new ScheduledTaskFinding(
                    taskName,
                    $"Runs from the Downloads folder. Author: {author}.");
            }

            if (scriptOrLolBin)
            {
                return new ScheduledTaskFinding(
                    taskName,
                    $"Uses a script or living-off-the-land executable. Author: {author}.");
            }

            return null;
        }

        private static string[] ParseCsvLine(string line)
        {
            List<string> values = new();
            bool quoted = false;
            int start = 0;

            for (int index = 0; index < line.Length; index++)
            {
                char current = line[index];
                if (current == '"')
                {
                    quoted = !quoted;
                }
                else if (current == ',' && !quoted)
                {
                    values.Add(Unquote(line[start..index]));
                    start = index + 1;
                }
            }

            values.Add(Unquote(line[start..]));
            return values.ToArray();
        }

        private static string Unquote(string value) =>
            value.Trim().Trim('"').Replace("\"\"", "\"");

        private static int FindColumn(string[] headers, params string[] names)
        {
            for (int index = 0; index < headers.Length; index++)
            {
                foreach (string name in names)
                {
                    if (headers[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return index;
                    }
                }
            }

            return -1;
        }

        private static string GetValue(string[] values, int index, string fallback) =>
            index >= 0 && index < values.Length && !string.IsNullOrWhiteSpace(values[index])
                ? values[index]
                : fallback;

        private sealed record ScheduledTaskFinding(string TaskName, string Reason);

        public sealed record ScheduledTaskSnapshot(
            int TotalTaskCount,
            int ReviewTaskCount,
            string PrimaryTaskName,
            string PrimaryReason)
        {
            public static ScheduledTaskSnapshot Unavailable { get; } =
                new(0, 0, "Unavailable", "Scheduled-task evidence could not be collected.");
        }
    }
}
