/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Reviews running-process command lines for investigation evidence.
    /// A command-line finding is not actionable by itself and must be correlated
    /// with process, lineage, persistence, or network evidence.
    /// </summary>
    public sealed class CommandLineMonitor
    {
        private static readonly string[] EncodedCommandIndicators =
        {
            " -enc ", " -encodedcommand ", " -e "
        };

        private static readonly string[] DownloadIndicators =
        {
            "downloadstring(", "downloadfile(", "invoke-webrequest", "iwr ",
            "curl ", "wget ", "start-bitstransfer", "http://", "https://"
        };

        private static readonly string[] ExecutionIndicators =
        {
            "invoke-expression", "iex ", "frombase64string", "reflection.assembly",
            "rundll32", "regsvr32", "mshta", "javascript:", "vbscript:"
        };

        public CommandLineSnapshot GetSnapshot()
        {
            List<CommandLineFinding> findings = new();
            int reviewedCount = 0;

            try
            {
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoLogo -NoProfile -NonInteractive -Command \"Get-CimInstance Win32_Process | Select-Object ProcessId,Name,CommandLine | ConvertTo-Json -Compress\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                if (!process.Start())
                {
                    return Empty("Command-line data was unavailable.");
                }

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10000);

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    return Empty("Command-line data was unavailable.");
                }

                using JsonDocument document = JsonDocument.Parse(output);
                IEnumerable<JsonElement> items = document.RootElement.ValueKind == JsonValueKind.Array
                    ? document.RootElement.EnumerateArray()
                    : new[] { document.RootElement };

                foreach (JsonElement item in items)
                {
                    string name = GetString(item, "Name");
                    string commandLine = GetString(item, "CommandLine");
                    if (string.IsNullOrWhiteSpace(commandLine))
                    {
                        continue;
                    }

                    reviewedCount++;
                    string normalized = $" {commandLine.ToLowerInvariant()} ";
                    bool encoded = ContainsAny(normalized, EncodedCommandIndicators);
                    bool downloads = ContainsAny(normalized, DownloadIndicators);
                    bool executes = ContainsAny(normalized, ExecutionIndicators);

                    string? reason = null;
                    if (encoded && (downloads || executes))
                    {
                        reason = "An encoded command is combined with download or execution behavior.";
                    }
                    else if (downloads && executes)
                    {
                        reason = "The command combines network retrieval with direct execution behavior.";
                    }

                    if (reason is not null)
                    {
                        findings.Add(new CommandLineFinding(
                            NormalizeProcessName(name),
                            reason,
                            RedactAndShorten(commandLine)));
                    }
                }
            }
            catch
            {
                return Empty("Command-line data was unavailable.");
            }

            CommandLineFinding? primary = findings.Count > 0 ? findings[0] : null;
            return new CommandLineSnapshot(
                reviewedCount,
                findings.Count,
                primary?.ProcessName ?? "None",
                primary?.Reason ?? "No unusual command-line combinations were detected.",
                primary?.CommandLineSummary ?? "None");
        }

        private static string GetString(JsonElement item, string propertyName)
        {
            return item.TryGetProperty(propertyName, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool ContainsAny(string value, IEnumerable<string> indicators)
        {
            foreach (string indicator in indicators)
            {
                if (value.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeProcessName(string name)
        {
            string value = name.Trim();
            return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? value[..^4]
                : value;
        }

        private static string RedactAndShorten(string commandLine)
        {
            string value = commandLine.Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
            return value.Length <= 180 ? value : value[..177] + "...";
        }

        private static CommandLineSnapshot Empty(string reason) =>
            new(0, 0, "None", reason, "None");

        private sealed record CommandLineFinding(
            string ProcessName,
            string Reason,
            string CommandLineSummary);

        public sealed record CommandLineSnapshot(
            int ReviewedProcessCount,
            int ReviewFindingCount,
            string PrimaryProcessName,
            string PrimaryReason,
            string PrimaryCommandLineSummary);
    }
}
