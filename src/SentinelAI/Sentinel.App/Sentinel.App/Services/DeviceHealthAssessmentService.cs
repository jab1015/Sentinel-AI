/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Read-only device/driver health assessment. Sentinel uses Windows-native
    /// Plug and Play diagnostics to identify devices reporting real problems.
    /// This service never disables, removes, updates, or rolls back a driver.
    /// </summary>
    public sealed class DeviceHealthAssessmentService
    {
        public DeviceHealthAssessment Assess()
        {
            CommandResult devices = Run(
                "pnputil.exe",
                "/enum-devices /problem");

            if (devices.ExitCode != 0)
            {
                return new DeviceHealthAssessment(
                    false,
                    Array.Empty<DeviceProblemEvidence>(),
                    false,
                    "Sentinel could not verify Windows device health. No automatic driver action is warranted.",
                    devices.Output,
                    devices.Error);
            }

            IReadOnlyList<DeviceProblemEvidence> problems = ParseProblems(devices.Output);
            bool investigationWarranted = problems.Count > 0;

            string summary = investigationWarranted
                ? $"Windows reports {problems.Count} device problem(s). Sentinel should correlate the affected device and problem code before recommending any repair."
                : "Windows reports no Plug and Play device problems requiring attention.";

            return new DeviceHealthAssessment(
                true,
                problems,
                investigationWarranted,
                summary,
                devices.Output,
                devices.Error);
        }

        private static IReadOnlyList<DeviceProblemEvidence> ParseProblems(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return Array.Empty<DeviceProblemEvidence>();

            var results = new List<DeviceProblemEvidence>();
            string instanceId = string.Empty;
            string description = string.Empty;
            string className = string.Empty;
            string manufacturer = string.Empty;
            string problem = string.Empty;
            int problemCode = -1;

            void Flush()
            {
                if (!string.IsNullOrWhiteSpace(instanceId) || !string.IsNullOrWhiteSpace(description))
                {
                    results.Add(new DeviceProblemEvidence(
                        instanceId,
                        description,
                        className,
                        manufacturer,
                        problemCode,
                        problem));
                }

                instanceId = string.Empty;
                description = string.Empty;
                className = string.Empty;
                manufacturer = string.Empty;
                problem = string.Empty;
                problemCode = -1;
            }

            foreach (string rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    Flush();
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;

                string key = line[..colon].Trim();
                string value = line[(colon + 1)..].Trim();

                if (key.Equals("Instance ID", StringComparison.OrdinalIgnoreCase))
                    instanceId = value;
                else if (key.Equals("Device Description", StringComparison.OrdinalIgnoreCase))
                    description = value;
                else if (key.Equals("Class Name", StringComparison.OrdinalIgnoreCase))
                    className = value;
                else if (key.Equals("Manufacturer Name", StringComparison.OrdinalIgnoreCase))
                    manufacturer = value;
                else if (key.Equals("Problem Code", StringComparison.OrdinalIgnoreCase))
                {
                    problem = value;
                    string digits = new(value.Where(char.IsDigit).ToArray());
                    if (!string.IsNullOrWhiteSpace(digits) &&
                        int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        problemCode = parsed;
                    }
                }
                else if (key.Equals("Problem", StringComparison.OrdinalIgnoreCase))
                    problem = value;
            }

            Flush();

            return results
                .Where(item => item.ProblemCode != 0 || !string.IsNullOrWhiteSpace(item.ProblemDescription))
                .GroupBy(item => item.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(50)
                .ToArray();
        }

        private static CommandResult Run(string fileName, string arguments)
        {
            try
            {
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    return new CommandResult(-1, output, "Device-health diagnostic timed out.");
                }

                return new CommandResult(process.ExitCode, output, error);
            }
            catch (Exception ex)
            {
                return new CommandResult(-1, string.Empty, ex.Message);
            }
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record DeviceHealthAssessment(
        bool DeviceHealthVerified,
        IReadOnlyList<DeviceProblemEvidence> Problems,
        bool RepairInvestigationWarranted,
        string Summary,
        string DiagnosticOutput,
        string DiagnosticError);

    public sealed record DeviceProblemEvidence(
        string InstanceId,
        string DeviceDescription,
        string ClassName,
        string Manufacturer,
        int ProblemCode,
        string ProblemDescription);
}
