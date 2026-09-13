/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.IO;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Read-only assessment of the active Windows power plan. This service never
    /// changes the selected plan or any processor/power settings.
    /// </summary>
    public sealed class PowerPlanHealthAssessmentService
    {
        public PowerPlanHealthAssessment Assess()
        {
            CommandResult active = Run("powercfg.exe", "/getactivescheme");

            if (active.ExitCode != 0 || string.IsNullOrWhiteSpace(active.Output))
            {
                return new PowerPlanHealthAssessment(
                    string.Empty,
                    string.Empty,
                    PowerPlanCategory.Unknown,
                    false,
                    false,
                    "Sentinel could not verify the active Windows power plan. No automatic power-plan change is warranted.",
                    active.Output,
                    active.Error);
            }

            string planGuid = ExtractGuid(active.Output);
            string planName = ExtractPlanName(active.Output);
            PowerPlanCategory category = Classify(planGuid, planName);
            bool optimizationInvestigationWarranted = category == PowerPlanCategory.PowerSaver;

            string summary = category switch
            {
                PowerPlanCategory.PowerSaver =>
                    "Windows is using the Power saver plan. Sentinel should verify device type and performance evidence before considering a change.",
                PowerPlanCategory.Balanced =>
                    "Windows is using the Balanced power plan. No power-plan optimization is warranted from plan selection alone.",
                PowerPlanCategory.HighPerformance =>
                    "Windows is using a performance-oriented power plan. No automatic power-plan change is warranted.",
                _ =>
                    "Sentinel verified the active power plan but could not classify it safely. No automatic change is warranted."
            };

            return new PowerPlanHealthAssessment(
                planGuid,
                planName,
                category,
                true,
                optimizationInvestigationWarranted,
                summary,
                active.Output,
                active.Error);
        }

        private static string ExtractGuid(string output)
        {
            const string marker = "GUID:";
            int start = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return string.Empty;
            start += marker.Length;
            int end = output.IndexOf(' ', start);
            string value = end > start ? output.Substring(start, end - start) : output[start..];
            return value.Trim();
        }

        private static string ExtractPlanName(string output)
        {
            int open = output.LastIndexOf('(');
            int close = output.LastIndexOf(')');
            if (open < 0 || close <= open) return string.Empty;
            return output.Substring(open + 1, close - open - 1).Trim();
        }

        private static PowerPlanCategory Classify(string guid, string name)
        {
            if (guid.Equals("a1841308-3541-4fab-bc81-f71556f20b4a", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Power saver", StringComparison.OrdinalIgnoreCase))
                return PowerPlanCategory.PowerSaver;

            if (guid.Equals("381b4222-f694-41f0-9685-ff5bb260df2e", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Balanced", StringComparison.OrdinalIgnoreCase))
                return PowerPlanCategory.Balanced;

            if (guid.Equals("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase) ||
                guid.Equals("e9a42b02-d5df-448d-aa00-03f14749eb61", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("High performance", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase))
                return PowerPlanCategory.HighPerformance;

            return PowerPlanCategory.Unknown;
        }

        private static CommandResult Run(string fileName, params string[] arguments)
        {
            try
            {
                string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                ProcessStartInfo startInfo = new()
                {
                    FileName = string.IsNullOrWhiteSpace(system) ? fileName : Path.Combine(system, fileName),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (string argument in arguments)
                    startInfo.ArgumentList.Add(argument);

                ProcessExecutionResult execution = BoundedProcessRunner
                    .RunAsync(startInfo, TimeSpan.FromSeconds(5), maxOutputChars: 64_000)
                    .GetAwaiter()
                    .GetResult();

                return new CommandResult(
                    execution.Succeeded ? 0 : execution.ExitCode ?? -1,
                    execution.StandardOutput,
                    execution.StandardError.Length > 0 ? execution.StandardError : execution.Detail);
            }
            catch (Exception ex)
            {
                return new CommandResult(-1, string.Empty, ex.GetType().Name);
            }
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record PowerPlanHealthAssessment(
        string ActivePlanGuid,
        string ActivePlanName,
        PowerPlanCategory Category,
        bool ActivePlanVerified,
        bool OptimizationInvestigationWarranted,
        string Summary,
        string DiagnosticOutput,
        string DiagnosticError);

    public enum PowerPlanCategory
    {
        Unknown,
        PowerSaver,
        Balanced,
        HighPerformance
    }
}
