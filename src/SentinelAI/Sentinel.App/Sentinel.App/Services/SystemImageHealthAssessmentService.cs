/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    public sealed class SystemImageHealthAssessmentService
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(30);

        public async Task<SystemImageHealthAssessment> AssessAsync(CancellationToken cancellationToken = default)
        {
            CommandResult dism = await RunAsync("dism.exe", "/Online /Cleanup-Image /ScanHealth", cancellationToken).ConfigureAwait(false);
            IntegrityAssessmentState componentState = ClassifyDism(dism);

            if (dism.Outcome is ProcessExecutionOutcome.TimedOut or ProcessExecutionOutcome.Canceled)
            {
                return new SystemImageHealthAssessment(
                    false, false, false, false, false,
                    "Windows component-store assessment did not complete. Sentinel skipped SFC and will not infer integrity health.",
                    dism.ExitCode, dism.Output, dism.Error, -1, string.Empty,
                    "SFC verification was skipped because DISM did not complete.")
                {
                    ComponentStoreState = componentState,
                    ProtectedFilesState = IntegrityAssessmentState.Unknown
                };
            }

            CommandResult sfc = await RunAsync("sfc.exe", "/verifyonly", cancellationToken).ConfigureAwait(false);
            IntegrityAssessmentState protectedState = ClassifySfc(sfc);

            bool componentHealthy = componentState == IntegrityAssessmentState.Healthy;
            bool componentCorrupt = componentState == IntegrityAssessmentState.Corrupt;
            bool filesHealthy = protectedState == IntegrityAssessmentState.Healthy;
            bool filesCorrupt = protectedState == IntegrityAssessmentState.Corrupt;
            bool repairWarranted = componentCorrupt || filesCorrupt;

            string summary = repairWarranted
                ? "Sentinel detected explicit Windows component or protected-file corruption evidence that warrants a repair plan. No repair has been performed yet."
                : componentHealthy && filesHealthy
                    ? "Windows component-store and protected-system-file integrity checks explicitly reported healthy results. No repair is warranted."
                    : "Sentinel could not positively verify Windows image integrity. No automatic repair will be attempted from unknown or failed evidence.";

            return new SystemImageHealthAssessment(
                componentHealthy, componentCorrupt, filesHealthy, filesCorrupt, repairWarranted,
                summary, dism.ExitCode, dism.Output, dism.Error, sfc.ExitCode, sfc.Output, sfc.Error)
            {
                ComponentStoreState = componentState,
                ProtectedFilesState = protectedState
            };
        }

        internal static IntegrityAssessmentState ClassifyDismText(int exitCode, string output, string error = "") =>
            ClassifyDism(new CommandResult(exitCode, output, error,
                exitCode == 0 ? ProcessExecutionOutcome.Succeeded : ProcessExecutionOutcome.NonZeroExit));

        internal static IntegrityAssessmentState ClassifySfcText(int exitCode, string output, string error = "") =>
            ClassifySfc(new CommandResult(exitCode, output, error,
                exitCode == 0 ? ProcessExecutionOutcome.Succeeded : ProcessExecutionOutcome.NonZeroExit));

        private static IntegrityAssessmentState ClassifyDism(CommandResult result)
        {
            if (result.Outcome != ProcessExecutionOutcome.Succeeded || result.ExitCode != 0)
                return result.Outcome == ProcessExecutionOutcome.TimedOut ? IntegrityAssessmentState.TimedOut : IntegrityAssessmentState.Error;

            string combined = result.Output + "\n" + result.Error;
            if (combined.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase))
                return IntegrityAssessmentState.Healthy;
            if (combined.Contains("The component store is repairable", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("component store corruption detected", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("the component store has been corrupted", StringComparison.OrdinalIgnoreCase))
                return IntegrityAssessmentState.Corrupt;

            return IntegrityAssessmentState.Unknown;
        }

        private static IntegrityAssessmentState ClassifySfc(CommandResult result)
        {
            string combined = result.Output + "\n" + result.Error;
            if (result.Outcome == ProcessExecutionOutcome.TimedOut) return IntegrityAssessmentState.TimedOut;
            if (combined.Contains("Windows Resource Protection did not find any integrity violations", StringComparison.OrdinalIgnoreCase))
                return result.ExitCode == 0 ? IntegrityAssessmentState.Healthy : IntegrityAssessmentState.Error;
            if (combined.Contains("Windows Resource Protection found integrity violations", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("Windows Resource Protection found corrupt files", StringComparison.OrdinalIgnoreCase))
                return IntegrityAssessmentState.Corrupt;
            if (combined.Contains("could not perform the requested operation", StringComparison.OrdinalIgnoreCase))
                return IntegrityAssessmentState.Error;
            if (result.Outcome != ProcessExecutionOutcome.Succeeded || result.ExitCode != 0)
                return IntegrityAssessmentState.Error;
            return IntegrityAssessmentState.Unknown;
        }

        private static async Task<CommandResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string absolutePath = string.IsNullOrWhiteSpace(system) ? fileName : Path.Combine(system, fileName);
            ProcessStartInfo startInfo = new()
            {
                FileName = absolutePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            ProcessExecutionResult result = await BoundedProcessRunner.RunAsync(
                startInfo, CommandTimeout, cancellationToken).ConfigureAwait(false);
            return new CommandResult(result.ExitCode ?? -1, result.StandardOutput, result.StandardError, result.Outcome);
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error, ProcessExecutionOutcome Outcome);
    }

    public enum IntegrityAssessmentState
    {
        Unknown,
        Healthy,
        Corrupt,
        Error,
        TimedOut
    }

    public sealed record SystemImageHealthAssessment(
        bool ComponentStoreHealthy,
        bool ComponentStoreCorruptionDetected,
        bool ProtectedFilesHealthy,
        bool ProtectedFilesCorruptionDetected,
        bool RepairInvestigationWarranted,
        string Summary,
        int DismExitCode,
        string DismOutput,
        string DismError,
        int SfcExitCode,
        string SfcOutput,
        string SfcError)
    {
        public IntegrityAssessmentState ComponentStoreState { get; init; } = IntegrityAssessmentState.Unknown;
        public IntegrityAssessmentState ProtectedFilesState { get; init; } = IntegrityAssessmentState.Unknown;
    }
}
