/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Performs read-only Windows component-store and protected-system-file health
    /// assessment. No repair is attempted here. DISM ScanHealth and SFC VerifyOnly
    /// are used because they do not intentionally modify Windows.
    /// </summary>
    public sealed class SystemImageHealthAssessmentService
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(30);
        public async Task<SystemImageHealthAssessment> AssessAsync(
            CancellationToken cancellationToken = default)
        {
            CommandResult dism = await RunAsync(
                "dism.exe",
                "/Online /Cleanup-Image /ScanHealth",
                cancellationToken).ConfigureAwait(false);

            bool componentStoreCorruptionDetected =
                ContainsAny(dism.Output, dism.Error,
                    "component store is repairable",
                    "component store corruption detected",
                    "the component store has been corrupted");

            bool componentStoreHealthy =
                dism.ExitCode == 0 &&
                ContainsAny(dism.Output, dism.Error,
                    "No component store corruption detected",
                    "The component store is repairable") == false &&
                !componentStoreCorruptionDetected;

            CommandResult sfc = await RunAsync(
                "sfc.exe",
                "/verifyonly",
                cancellationToken).ConfigureAwait(false);

            bool protectedFilesCorruptionDetected =
                ContainsAny(sfc.Output, sfc.Error,
                    "found integrity violations",
                    "found corrupt files",
                    "could not perform the requested operation");

            bool protectedFilesHealthy =
                sfc.ExitCode == 0 &&
                ContainsAny(sfc.Output, sfc.Error,
                    "did not find any integrity violations");

            bool repairInvestigationWarranted =
                componentStoreCorruptionDetected || protectedFilesCorruptionDetected;

            string summary = repairInvestigationWarranted
                ? "Sentinel detected Windows component or protected-file integrity evidence that warrants a repair plan. No repair has been performed yet."
                : componentStoreHealthy && protectedFilesHealthy
                    ? "Windows component-store and protected-system-file integrity checks passed. No repair is warranted."
                    : "Sentinel could not fully verify Windows image integrity. No automatic repair will be attempted from incomplete evidence.";

            return new SystemImageHealthAssessment(
                componentStoreHealthy,
                componentStoreCorruptionDetected,
                protectedFilesHealthy,
                protectedFilesCorruptionDetected,
                repairInvestigationWarranted,
                summary,
                dism.ExitCode,
                dism.Output,
                dism.Error,
                sfc.ExitCode,
                sfc.Output,
                sfc.Error);
        }

        private static bool ContainsAny(string output, string error, params string[] values)
        {
            string combined = $"{output}\n{error}";
            foreach (string value in values)
            {
                if (combined.Contains(value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static async Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            CancellationToken cancellationToken)
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
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeoutSource = new(CommandTimeout);
            using CancellationTokenSource linkedSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

            try
            {
                await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }

                return new CommandResult(
                    -1,
                    string.Empty,
                    $"Windows Windows integrity assessment exceeded its {CommandTimeout.TotalSeconds:0}-second safety timeout.");
            }

            return new CommandResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
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
        string SfcError);
}
