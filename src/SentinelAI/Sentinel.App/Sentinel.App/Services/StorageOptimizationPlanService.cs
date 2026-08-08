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
    /// Builds a read-only storage optimization plan from verified media type,
    /// health, TRIM state, and Windows' own volume analysis. This service never
    /// changes the drive.
    /// </summary>
    public sealed class StorageOptimizationPlanService
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);
        private readonly StorageOptimizationAssessmentService _assessmentService = new();

        public async Task<StorageOptimizationPlan> BuildSystemDrivePlanAsync(
            CancellationToken cancellationToken = default)
        {
            StorageOptimizationAssessment assessment =
                await _assessmentService.AssessSystemDriveAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (!assessment.SafeToConsiderNativeOptimization)
            {
                return StorageOptimizationPlan.NoAction(
                    assessment,
                    "Sentinel will not optimize this drive because its media type or health state is not sufficiently verified.");
            }

            if (assessment.MediaKind == StorageMediaKind.SolidState &&
                assessment.TrimStateKnown &&
                !assessment.TrimEnabled)
            {
                return StorageOptimizationPlan.NoAction(
                    assessment,
                    "Sentinel will not run SSD optimization until TRIM support is verified as enabled.");
            }

            CommandResult analysis = await RunAsync(
                "defrag.exe",
                $"{assessment.DriveLetter}: /A /V /U",
                cancellationToken).ConfigureAwait(false);

            if (analysis.ExitCode != 0)
            {
                return StorageOptimizationPlan.NoAction(
                    assessment,
                    "Windows drive analysis did not complete successfully, so Sentinel will not make a storage change.",
                    analysis.Output,
                    analysis.Error);
            }

            string output = analysis.Output ?? string.Empty;
            bool optimizationRecommended =
                Contains(output, "You should defragment this volume") ||
                Contains(output, "optimization is recommended") ||
                Contains(output, "needs optimization") ||
                Contains(output, "fragmented") && !Contains(output, "0% fragmented");

            if (!optimizationRecommended)
            {
                return StorageOptimizationPlan.NoAction(
                    assessment,
                    "Windows analysis did not provide verified evidence that drive optimization is currently needed.",
                    output,
                    analysis.Error);
            }

            return assessment.MediaKind switch
            {
                StorageMediaKind.SolidState => new StorageOptimizationPlan(
                    assessment,
                    true,
                    StorageOptimizationAction.Retrim,
                    $"{assessment.DriveLetter}: /L /U /V",
                    "Windows analysis indicates optimization is warranted. Sentinel will use retrim for this solid-state drive, not routine traditional defragmentation.",
                    output,
                    analysis.Error),

                StorageMediaKind.HardDisk => new StorageOptimizationPlan(
                    assessment,
                    true,
                    StorageOptimizationAction.Defragment,
                    $"{assessment.DriveLetter}: /D /U /V",
                    "Windows analysis indicates meaningful optimization is warranted. Sentinel may use Windows' native defragmentation for this hard disk.",
                    output,
                    analysis.Error),

                _ => StorageOptimizationPlan.NoAction(
                    assessment,
                    "Sentinel could not verify the drive media type, so no optimization action was selected.",
                    output,
                    analysis.Error)
            };
        }

        private static bool Contains(string value, string text) =>
            value.Contains(text, StringComparison.OrdinalIgnoreCase);

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
                    $"Windows drive analysis exceeded its {CommandTimeout.TotalSeconds:0}-second safety timeout.");
            }

            return new CommandResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record StorageOptimizationPlan(
        StorageOptimizationAssessment Assessment,
        bool ActionWarranted,
        StorageOptimizationAction Action,
        string DefragArguments,
        string Summary,
        string AnalysisOutput,
        string AnalysisError)
    {
        public static StorageOptimizationPlan NoAction(
            StorageOptimizationAssessment assessment,
            string summary,
            string analysisOutput = "",
            string analysisError = "") =>
            new(
                assessment,
                false,
                StorageOptimizationAction.None,
                string.Empty,
                summary,
                analysisOutput,
                analysisError);
    }

    public enum StorageOptimizationAction
    {
        None,
        Retrim,
        Defragment
    }
}
