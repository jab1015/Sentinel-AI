/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Performs a non-destructive end-to-end verification of Sentinel's optimization
    /// and repair subsystems. This service does not execute any repair or optimization.
    /// It verifies that each assessment/planning layer can initialize and produce
    /// a valid result on the current computer.
    /// </summary>
    public sealed class OptimizationRuntimeVerificationService
    {
        public async Task<OptimizationRuntimeVerificationResult> VerifyAsync(
            CancellationToken cancellationToken = default)
        {
            var checks = new List<OptimizationRuntimeCheck>();

            await RunCheckAsync(
                checks,
                "Optimization settings",
                () => Task.FromResult(VerifySettings()),
                cancellationToken).ConfigureAwait(false);

            await RunCheckAsync(
                checks,
                "Startup assessment",
                () => Task.FromResult(VerifyStartup()),
                cancellationToken).ConfigureAwait(false);

            await RunCheckAsync(
                checks,
                "Windows service health",
                () => Task.FromResult(VerifyServices()),
                cancellationToken).ConfigureAwait(false);

            await RunCheckAsync(
                checks,
                "Network health",
                VerifyNetworkAsync,
                cancellationToken).ConfigureAwait(false);

            await RunCheckAsync(
                checks,
                "Storage optimization planning",
                VerifyStorageAsync,
                cancellationToken).ConfigureAwait(false);

            bool passed = checks.TrueForAll(check => check.Passed);

            return new OptimizationRuntimeVerificationResult(
                passed,
                checks,
                passed
                    ? "All optimization and repair subsystems passed non-destructive runtime verification."
                    : "One or more optimization subsystems could not be verified. Sentinel should not treat the optimization expansion as release-ready until the failed check is resolved.");
        }

        private static OptimizationRuntimeCheck VerifySettings()
        {
            OptimizationSettings settings = new OptimizationSettingsService().Load();
            return new OptimizationRuntimeCheck(
                "Optimization settings",
                true,
                $"Mode={settings.Mode}; Automatic={settings.AutomaticOptimizationEnabled}; Verify={settings.VerifyEveryChange}; Rollback={settings.RollBackWhenPossible}");
        }

        private static OptimizationRuntimeCheck VerifyStartup()
        {
            StartupPerformanceAssessment assessment = new StartupPerformanceAssessmentService().Assess();
            return new OptimizationRuntimeCheck(
                "Startup assessment",
                assessment.Entries is not null,
                assessment.Summary);
        }

        private static OptimizationRuntimeCheck VerifyServices()
        {
            WindowsServiceHealthAssessment assessment = new WindowsServiceHealthAssessmentService().Assess();
            bool valid = assessment.Services is not null && assessment.Services.Count > 0;
            return new OptimizationRuntimeCheck(
                "Windows service health",
                valid,
                assessment.Summary);
        }

        private static async Task<OptimizationRuntimeCheck> VerifyNetworkAsync()
        {
            NetworkHealthAssessment assessment =
                await new NetworkHealthAssessmentService().AssessAsync().ConfigureAwait(false);

            bool valid = !string.IsNullOrWhiteSpace(assessment.Summary);
            return new OptimizationRuntimeCheck(
                "Network health",
                valid,
                assessment.Summary);
        }

        private static async Task<OptimizationRuntimeCheck> VerifyStorageAsync()
        {
            StorageOptimizationPlan plan =
                await new StorageOptimizationPlanService().BuildSystemDrivePlanAsync().ConfigureAwait(false);

            bool valid = !string.IsNullOrWhiteSpace(plan.Summary);
            return new OptimizationRuntimeCheck(
                "Storage optimization planning",
                valid,
                plan.Summary);
        }

        private static async Task RunCheckAsync(
            List<OptimizationRuntimeCheck> checks,
            string name,
            Func<Task<OptimizationRuntimeCheck>> action,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                OptimizationRuntimeCheck check = await action().ConfigureAwait(false);
                checks.Add(check);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                checks.Add(new OptimizationRuntimeCheck(
                    name,
                    false,
                    $"{ex.GetType().Name}: {ex.Message}"));
            }
        }
    }

    public sealed record OptimizationRuntimeVerificationResult(
        bool Passed,
        IReadOnlyList<OptimizationRuntimeCheck> Checks,
        string Summary);

    public sealed record OptimizationRuntimeCheck(
        string Name,
        bool Passed,
        string Details);
}
