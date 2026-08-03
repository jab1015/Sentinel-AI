/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Coordinates Sentinel's verified Windows maintenance executors. At most one
    /// automatic system change is attempted per maintenance cycle. Service repair
    /// is evaluated first, then low-risk network repair, then native storage
    /// optimization. All underlying executors revalidate and verify their work.
    /// </summary>
    public sealed class IntegratedMaintenanceCoordinator
    {
        private static readonly TimeSpan MinimumEvaluationInterval = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan MinimumChangeInterval = TimeSpan.FromHours(12);

        private readonly OptimizationSettingsService _settingsService = new();
        private readonly WindowsServiceRepairPlanService _servicePlanService = new();
        private readonly WindowsServiceRepairSafetyService _serviceSafetyService = new();
        private readonly WindowsServiceRepairExecutor _serviceExecutor = new();
        private readonly NetworkRepairExecutor _networkExecutor = new();
        private readonly StorageOptimizationExecutor _storageExecutor = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _statePath;

        public IntegratedMaintenanceCoordinator()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Modern Methods",
                "Sentinel AI");

            Directory.CreateDirectory(directory);
            _statePath = Path.Combine(directory, "integrated-maintenance-state.json");
        }

        public async Task<IntegratedMaintenanceResult> EvaluateAndRunAsync(
            CancellationToken cancellationToken = default)
        {
            if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return IntegratedMaintenanceResult.NotRun("A maintenance evaluation is already in progress.");

            try
            {
                MaintenanceState state = LoadState();
                DateTimeOffset now = DateTimeOffset.UtcNow;

                if (state.LastEvaluationUtc.HasValue &&
                    now - state.LastEvaluationUtc.Value < MinimumEvaluationInterval)
                {
                    return IntegratedMaintenanceResult.NotRun(
                        "Sentinel recently completed a maintenance evaluation.");
                }

                SaveState(state with { LastEvaluationUtc = now });

                OptimizationSettings settings = _settingsService.Load();
                if (!settings.AutomaticOptimizationEnabled)
                {
                    return IntegratedMaintenanceResult.NotRun(
                        "Automatic optimization is turned off.");
                }

                bool changeAllowed =
                    !state.LastChangeUtc.HasValue ||
                    now - state.LastChangeUtc.Value >= MinimumChangeInterval;

                if (!changeAllowed)
                {
                    return IntegratedMaintenanceResult.NotRun(
                        "Sentinel recently made a verified system change and is waiting before making another.");
                }

                // 1. Core/security service repair gets highest priority.
                WindowsServiceRepairPlan servicePlan = _servicePlanService.BuildPlan();
                WindowsServiceRepairSafetyAssessment serviceSafety =
                    _serviceSafetyService.Evaluate(servicePlan, settings);

                if (serviceSafety.ExecutionAllowed)
                {
                    WindowsServiceRepairExecutionResult serviceResult =
                        await _serviceExecutor.ExecuteAsync(serviceSafety, cancellationToken)
                            .ConfigureAwait(false);

                    if (serviceResult.Attempted)
                    {
                        SaveState(new MaintenanceState(
                            now,
                            now,
                            "WindowsService",
                            serviceResult.Summary));

                        return new IntegratedMaintenanceResult(
                            true,
                            serviceResult.Verified,
                            "WindowsService",
                            serviceResult.Summary);
                    }
                }

                // 2. Only the low-risk DNS cache repair can pass the current network gate.
                NetworkRepairExecutionResult networkResult =
                    await _networkExecutor.EvaluateAndExecuteAsync(settings, cancellationToken)
                        .ConfigureAwait(false);

                if (networkResult.Attempted)
                {
                    SaveState(new MaintenanceState(
                        now,
                        now,
                        "Network",
                        networkResult.Summary));

                    return new IntegratedMaintenanceResult(
                        true,
                        networkResult.Verified,
                        "Network",
                        networkResult.Summary);
                }

                // 3. Native drive optimization is last because it can be longer-running.
                StorageOptimizationExecutionResult storageResult =
                    await _storageExecutor.EvaluateAndExecuteAsync(cancellationToken)
                        .ConfigureAwait(false);

                if (storageResult.Attempted)
                {
                    SaveState(new MaintenanceState(
                        now,
                        now,
                        "Storage",
                        storageResult.Summary));

                    return new IntegratedMaintenanceResult(
                        true,
                        storageResult.Verified,
                        "Storage",
                        storageResult.Summary);
                }

                SaveState(new MaintenanceState(
                    now,
                    state.LastChangeUtc,
                    string.Empty,
                    "No verified automatic maintenance action is currently warranted."));

                return IntegratedMaintenanceResult.NotRun(
                    "No verified automatic maintenance action is currently warranted.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return IntegratedMaintenanceResult.NotRun(
                    $"Automatic maintenance stopped safely before an unverified change ({ex.GetType().Name}).");
            }
            finally
            {
                _gate.Release();
            }
        }

        private MaintenanceState LoadState()
        {
            try
            {
                if (!File.Exists(_statePath))
                    return MaintenanceState.Empty;

                string json = File.ReadAllText(_statePath);
                return JsonSerializer.Deserialize<MaintenanceState>(json) ?? MaintenanceState.Empty;
            }
            catch
            {
                return MaintenanceState.Empty;
            }
        }

        private void SaveState(MaintenanceState state)
        {
            try
            {
                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_statePath, json);
            }
            catch
            {
                // State persistence must never cause Sentinel itself to fail.
            }
        }

        private sealed record MaintenanceState(
            DateTimeOffset? LastEvaluationUtc,
            DateTimeOffset? LastChangeUtc,
            string LastCategory,
            string LastSummary)
        {
            public static MaintenanceState Empty { get; } =
                new(null, null, string.Empty, string.Empty);
        }
    }

    public sealed record IntegratedMaintenanceResult(
        bool ChangeAttempted,
        bool Verified,
        string Category,
        string Summary)
    {
        public static IntegratedMaintenanceResult NotRun(string summary) =>
            new(false, false, string.Empty, summary);
    }
}
