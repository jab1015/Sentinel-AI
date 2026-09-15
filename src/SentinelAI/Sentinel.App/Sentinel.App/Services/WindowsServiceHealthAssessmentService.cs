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
    /// <summary>
    /// Read-only assessment of a small set of Windows services that directly
    /// support Sentinel's security and maintenance capabilities. A stopped service
    /// is evidence only; this layer never restarts or reconfigures Windows.
    /// </summary>
    public sealed class WindowsServiceHealthAssessmentService
    {
        private static readonly ServiceExpectation[] Expectations =
        {
            new("WinDefend", "Microsoft Defender Antivirus", ServiceImportance.Security),
            new("mpssvc", "Windows Defender Firewall", ServiceImportance.Security),
            new("wuauserv", "Windows Update", ServiceImportance.Maintenance),
            new("BITS", "Background Intelligent Transfer Service", ServiceImportance.Maintenance),
            new("EventLog", "Windows Event Log", ServiceImportance.Core),
            new("Schedule", "Task Scheduler", ServiceImportance.Core)
        };

        public WindowsServiceHealthAssessment Assess()
        {
            var evidence = new List<WindowsServiceEvidence>();

            foreach (ServiceExpectation expectation in Expectations)
            {
                ServiceQueryResult query = QueryService(expectation.Name);

                evidence.Add(new WindowsServiceEvidence(
                    expectation.Name,
                    expectation.DisplayName,
                    expectation.Importance,
                    query.Exists,
                    query.State,
                    query.StartMode,
                    DetermineConcern(expectation, query)));
            }

            WindowsServiceEvidence[] concerns = evidence
                .Where(item => item.Concern != ServiceHealthConcern.None)
                .ToArray();

            bool repairInvestigationWarranted = concerns.Any(item =>
                item.Concern == ServiceHealthConcern.UnexpectedlyStopped ||
                item.Concern == ServiceHealthConcern.Disabled);

            string summary = concerns.Length == 0
                ? "Sentinel found no verified service-health condition requiring action."
                : repairInvestigationWarranted
                    ? "Sentinel found a Windows service state that warrants correlation before any repair is attempted."
                    : "One or more service states could not be fully verified. Sentinel will not change Windows based on incomplete evidence.";

            return new WindowsServiceHealthAssessment(evidence, concerns, repairInvestigationWarranted, summary);
        }

        private static ServiceHealthConcern DetermineConcern(ServiceExpectation expectation, ServiceQueryResult query)
        {
            if (!query.Exists) return ServiceHealthConcern.Unverified;
            if (query.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return ServiceHealthConcern.Disabled;
            if (query.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase) &&
                (expectation.Importance == ServiceImportance.Security || expectation.Importance == ServiceImportance.Core))
                return ServiceHealthConcern.UnexpectedlyStopped;
            return ServiceHealthConcern.None;
        }

        private static ServiceQueryResult QueryService(string serviceName)
        {
            ProcessExecutionResult query = RunSc("query", serviceName);
            if (!query.Succeeded)
                return new ServiceQueryResult(false, "Unknown", "Unknown");

            string queryOutput = query.StandardOutput;
            string state = queryOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)
                ? "Running"
                : queryOutput.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)
                    ? "Stopped"
                    : "Unknown";

            string startMode = QueryStartMode(serviceName);
            return new ServiceQueryResult(true, state, startMode);
        }

        private static string QueryStartMode(string serviceName)
        {
            ProcessExecutionResult query = RunSc("qc", serviceName);
            if (!query.Succeeded) return "Unknown";

            string output = query.StandardOutput;
            if (output.Contains("DISABLED", StringComparison.OrdinalIgnoreCase)) return "Disabled";
            if (output.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase)) return "Automatic";
            if (output.Contains("DEMAND_START", StringComparison.OrdinalIgnoreCase)) return "Manual";
            return "Unknown";
        }

        private static ProcessExecutionResult RunSc(string verb, string serviceName)
        {
            try
            {
                string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                ProcessStartInfo startInfo = new()
                {
                    FileName = string.IsNullOrWhiteSpace(system) ? "sc.exe" : Path.Combine(system, "sc.exe"),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add(verb);
                startInfo.ArgumentList.Add(serviceName);
                return BoundedProcessRunner
                    .RunAsync(startInfo, TimeSpan.FromSeconds(3), maxOutputChars: 64_000)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                return ProcessExecutionResult.LaunchFailure("sc.exe query failed before execution completed.");
            }
        }

        private sealed record ServiceExpectation(string Name, string DisplayName, ServiceImportance Importance);
        private sealed record ServiceQueryResult(bool Exists, string State, string StartMode);
    }

    public sealed record WindowsServiceHealthAssessment(
        IReadOnlyList<WindowsServiceEvidence> Services,
        IReadOnlyList<WindowsServiceEvidence> Concerns,
        bool RepairInvestigationWarranted,
        string Summary);

    public sealed record WindowsServiceEvidence(
        string ServiceName,
        string DisplayName,
        ServiceImportance Importance,
        bool Exists,
        string State,
        string StartMode,
        ServiceHealthConcern Concern);

    public enum ServiceImportance
    {
        Core,
        Security,
        Maintenance
    }

    public enum ServiceHealthConcern
    {
        None,
        UnexpectedlyStopped,
        Disabled,
        Unverified
    }
}
