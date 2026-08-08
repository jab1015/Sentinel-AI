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
        private static readonly TimeSpan RuntimeVerificationInterval = TimeSpan.FromHours(24);
        private static readonly SemaphoreSlim EvaluationGate = new(1, 1);

        private readonly OptimizationSettingsService _settingsService = new();
        private readonly StoreSubscriptionService _subscriptionService = new();
        private readonly OptimizationRuntimeVerificationService _runtimeVerificationService = new();
        private readonly WindowsServiceRepairPlanService _servicePlanService = new();
        private readonly WindowsServiceRepairSafetyService _serviceSafetyService = new();
        private readonly WindowsServiceRepairExecutor _serviceExecutor = new();
        private readonly NetworkRepairExecutor _networkExecutor = new();
        private readonly StorageOptimizationExecutor _storageExecutor = new();
        private readonly MaintenanceOutcomeRecorder _outcomeRecorder = new();
        private readonly string _statePath;
        private readonly string _verificationPath;

        public IntegratedMaintenanceCoordinator()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Modern Methods",
                "Sentinel AI");

            Directory.CreateDirectory(directory);
            _statePath = Path.Combine(directory, "integrated-maintenance-state.json");
            _verificationPath = Path.Combine(directory, "optimization-runtime-verification.json");
        }

        public async Task<IntegratedMaintenanceResult> EvaluateAndRunAsync(
            CancellationToken cancellationToken = default)
        {
            if (!await EvaluationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return IntegratedMaintenanceResult.NotRun("A maintenance evaluation is already in progress.");

            try
            {
                SubscriptionState subscription = await _subscriptionService.GetStateAsync().ConfigureAwait(false);
                if (!subscription.IsActive)
                {
                    return IntegratedMaintenanceResult.NotRun(
                        "Free local monitoring remains active. An active Sentinel AI subscription is required before automatic maintenance can make system changes.");
                }

                MaintenanceState state = LoadState();
                DateTimeOffset now = DateTimeOffset.UtcNow;

                if (state.LastEvaluationUtc.HasValue &&
                    now - state.LastEvaluationUtc.Value < MinimumEvaluationInterval)
                {
                    return IntegratedMaintenanceResult.NotRun(
                        "Sentinel recently completed a maintenance evaluation.");
                }

                SaveState(state with { LastEvaluationUtc = now });

                RuntimeVerificationState verificationState = LoadVerificationState();
                bool verificationRequired =
                    !verificationState.LastVerifiedUtc.HasValue ||
                    now - verificationState.LastVerifiedUtc.Value >= RuntimeVerificationInterval ||
                    !verificationState.Passed;

                if (verificationRequired)
                {
                    OptimizationRuntimeVerificationResult verification =
                        await _runtimeVerificationService.VerifyAsync(cancellationToken)
                            .ConfigureAwait(false);

                    SaveVerificationState(new RuntimeVerificationState(
                        now,
                        verification.Passed,
                        verification.Summary));

                    if (!verification.Passed)
                    {
                        return IntegratedMaintenanceResult.NotRun(
                            "Sentinel blocked automatic maintenance because runtime verification did not pass. No system change was made.");
                    }
                }
                else if (!verificationState.Passed)
                {
                    return IntegratedMaintenanceResult.NotRun(
                        "Sentinel blocked automatic maintenance because the last runtime verification did not pass.");
                }

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
                        "Sentinel recently attempted automatic maintenance and is waiting before starting another system change.");
                }

                WindowsServiceRepairPlan servicePlan = _servicePlanService.BuildPlan();
                WindowsServiceRepairSafetyAssessment serviceSafety =
                    _serviceSafetyService.Evaluate(servicePlan, settings);

                if (serviceSafety.ExecutionAllowed)
                {
                    WindowsServiceRepairExecutionResult serviceResult =
                        await _serviceExecutor.ExecuteAsync(serviceSafety, cancellationToken)
                            .ConfigureAwait(false);
                    _outcomeRecorder.Record(serviceResult);

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

                NetworkRepairExecutionResult networkResult =
                    await _networkExecutor.EvaluateAndExecuteAsync(settings, cancellationToken)
                        .ConfigureAwait(false);
                _outcomeRecorder.Record(networkResult);

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

                StorageOptimizationExecutionResult storageResult =
                    await _storageExecutor.EvaluateAndExecuteAsync(cancellationToken)
                        .ConfigureAwait(false);
                _outcomeRecorder.Record(storageResult);

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
                EvaluationGate.Release();
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

        private RuntimeVerificationState LoadVerificationState()
        {
            try
            {
                if (!File.Exists(_verificationPath))
                    return RuntimeVerificationState.Empty;

                string json = File.ReadAllText(_verificationPath);
                return JsonSerializer.Deserialize<RuntimeVerificationState>(json) ?? RuntimeVerificationState.Empty;
            }
            catch
            {
                return RuntimeVerificationState.Empty;
            }
        }

        private void SaveState(MaintenanceState state) =>
            WriteJsonAtomically(_statePath, state);

        private void SaveVerificationState(RuntimeVerificationState state) =>
            WriteJsonAtomically(_verificationPath, state);

        private static void WriteJsonAtomically<T>(string path, T state)
        {
            string? temporaryPath = null;
            try
            {
                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                string directory = Path.GetDirectoryName(path)!;
                temporaryPath = Path.Combine(directory, $".maintenance-state.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, path, overwrite: true);
            }
            catch
            {
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporaryPath))
                {
                    try { File.Delete(temporaryPath); }
                    catch { }
                }
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

        private sealed record RuntimeVerificationState(
            DateTimeOffset? LastVerifiedUtc,
            bool Passed,
            string Summary)
        {
            public static RuntimeVerificationState Empty { get; } =
                new(null, false, string.Empty);
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
