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
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Modern Methods", "Sentinel AI");
            Directory.CreateDirectory(directory);
            _statePath = Path.Combine(directory, "integrated-maintenance-state.json");
            _verificationPath = Path.Combine(directory, "optimization-runtime-verification.json");
        }

        public async Task<IntegratedMaintenanceResult> EvaluateAndRunAsync(CancellationToken cancellationToken = default)
        {
            if (!await EvaluationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return IntegratedMaintenanceResult.NotRun("A maintenance evaluation is already in progress.");

            bool changeReservationPersisted = false;
            try
            {
                SubscriptionState subscription = await _subscriptionService.GetStateAsync().ConfigureAwait(false);
                if (!subscription.IsActive)
                    return IntegratedMaintenanceResult.NotRun("Free local monitoring remains active. An active Sentinel AI subscription is required before automatic maintenance can make system changes.");

                PersistenceReadResult<MaintenanceState> stateRead = LoadState();
                if (!stateRead.Succeeded)
                    return IntegratedMaintenanceResult.NotRun("Sentinel blocked automatic maintenance because its safety/cooldown state could not be read reliably. No system change was attempted.");
                MaintenanceState state = stateRead.Value!;
                DateTimeOffset now = DateTimeOffset.UtcNow;

                if (state.LastEvaluationUtc.HasValue && now - state.LastEvaluationUtc.Value < MinimumEvaluationInterval)
                    return IntegratedMaintenanceResult.NotRun("Sentinel recently completed a maintenance evaluation.");

                if (!TrySaveState(state with { LastEvaluationUtc = now }))
                    return IntegratedMaintenanceResult.NotRun("Sentinel blocked automatic maintenance because it could not persist the evaluation safety state. No system change was attempted.");

                PersistenceReadResult<RuntimeVerificationState> verificationRead = LoadVerificationState();
                if (!verificationRead.Succeeded)
                    return IntegratedMaintenanceResult.NotRun("Sentinel blocked automatic maintenance because runtime-verification state is unavailable or corrupt.");
                RuntimeVerificationState verificationState = verificationRead.Value!;

                bool verificationRequired = !verificationState.LastVerifiedUtc.HasValue ||
                    now - verificationState.LastVerifiedUtc.Value >= RuntimeVerificationInterval || !verificationState.Passed;

                if (verificationRequired)
                {
                    OptimizationRuntimeVerificationResult verification = await _runtimeVerificationService.VerifyAsync(cancellationToken).ConfigureAwait(false);
                    if (!TrySaveVerificationState(new RuntimeVerificationState(now, verification.Passed, verification.Summary)))
                        return IntegratedMaintenanceResult.NotRun("Sentinel blocked automatic maintenance because the runtime-verification result could not be persisted.");
                    if (!verification.Passed)
                        return IntegratedMaintenanceResult.NotRun("Sentinel blocked automatic maintenance because runtime verification did not pass. No system change was made.");
                }
                else if (!verificationState.Passed)
                {
                    return IntegratedMaintenanceResult.NotRun("Sentinel blocked automatic maintenance because the last runtime verification did not pass.");
                }

                OptimizationSettings settings = _settingsService.Load();
                if (!settings.AutomaticOptimizationEnabled)
                    return IntegratedMaintenanceResult.NotRun("Automatic optimization is turned off.");

                if (state.LastChangeUtc.HasValue && now - state.LastChangeUtc.Value < MinimumChangeInterval)
                    return IntegratedMaintenanceResult.NotRun("Sentinel recently attempted automatic maintenance and is waiting before starting another system change.");

                WindowsServiceRepairPlan servicePlan = _servicePlanService.BuildPlan();
                WindowsServiceRepairSafetyAssessment serviceSafety = _serviceSafetyService.Evaluate(servicePlan, settings);
                if (serviceSafety.ExecutionAllowed)
                {
                    if (!ReserveChange(now, "WindowsService"))
                        return PersistenceBlocked();
                    changeReservationPersisted = true;
                    WindowsServiceRepairExecutionResult serviceResult = await _serviceExecutor.ExecuteAsync(serviceSafety, cancellationToken).ConfigureAwait(false);
                    _outcomeRecorder.Record(serviceResult);
                    PersistOutcomeBestEffort(now, "WindowsService", serviceResult.Summary);
                    if (serviceResult.Attempted)
                        return new IntegratedMaintenanceResult(true, serviceResult.Verified, "WindowsService", serviceResult.Summary);
                }

                NetworkRepairPlan networkPlan = await _networkExecutor.EvaluateAsync(settings, cancellationToken).ConfigureAwait(false);
                if (networkPlan.ExecutionWarranted)
                {
                    if (!ReserveChange(now, "Network"))
                        return PersistenceBlocked();
                    changeReservationPersisted = true;
                    NetworkRepairExecutionResult networkResult = await _networkExecutor.ExecuteAsync(networkPlan, cancellationToken).ConfigureAwait(false);
                    _outcomeRecorder.Record(networkResult);
                    PersistOutcomeBestEffort(now, "Network", networkResult.Summary);
                    if (networkResult.Attempted)
                        return new IntegratedMaintenanceResult(true, networkResult.Verified, "Network", networkResult.Summary);
                }

                StorageOptimizationPlan storagePlan = await _storageExecutor.EvaluateAsync(cancellationToken).ConfigureAwait(false);
                if (storagePlan.ExecutionWarranted)
                {
                    if (!ReserveChange(now, "Storage"))
                        return PersistenceBlocked();
                    changeReservationPersisted = true;
                    StorageOptimizationExecutionResult storageResult = await _storageExecutor.ExecuteAsync(storagePlan, cancellationToken).ConfigureAwait(false);
                    _outcomeRecorder.Record(storageResult);
                    PersistOutcomeBestEffort(now, "Storage", storageResult.Summary);
                    if (storageResult.Attempted)
                        return new IntegratedMaintenanceResult(true, storageResult.Verified, "Storage", storageResult.Summary);
                }

                if (!TrySaveState(new MaintenanceState(now, state.LastChangeUtc, string.Empty,
                    "No verified automatic maintenance action is currently warranted.")))
                    return IntegratedMaintenanceResult.NotRun("No maintenance action was warranted, but Sentinel could not persist the evaluation outcome. Future unattended changes will remain fail-closed.");

                return IntegratedMaintenanceResult.NotRun("No verified automatic maintenance action is currently warranted.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return changeReservationPersisted
                    ? new IntegratedMaintenanceResult(true, false, "Unknown",
                        $"Automatic maintenance encountered {ex.GetType().Name} after a durable change reservation. Sentinel will not claim that no change occurred and will preserve the cooldown until the outcome is verified.")
                    : IntegratedMaintenanceResult.NotRun($"Automatic maintenance stopped before any reserved system change ({ex.GetType().Name}).");
            }
            finally { EvaluationGate.Release(); }
        }

        private bool ReserveChange(DateTimeOffset now, string category) =>
            TrySaveState(new MaintenanceState(now, now, category, "Change reserved before execution; final outcome pending verification."));

        private void PersistOutcomeBestEffort(DateTimeOffset now, string category, string summary)
        {
            // Failure does not clear the prior reservation, so restart/cooldown remains fail-closed.
            _ = TrySaveState(new MaintenanceState(now, now, category, summary));
        }

        private IntegratedMaintenanceResult PersistenceBlocked() =>
            IntegratedMaintenanceResult.NotRun("Sentinel blocked automatic maintenance because it could not durably reserve the change before execution.");

        private PersistenceReadResult<MaintenanceState> LoadState() => ReadState(_statePath, MaintenanceState.Empty);
        private PersistenceReadResult<RuntimeVerificationState> LoadVerificationState() => ReadState(_verificationPath, RuntimeVerificationState.Empty);
        private bool TrySaveState(MaintenanceState state) => WriteJsonAtomically(_statePath, state);
        private bool TrySaveVerificationState(RuntimeVerificationState state) => WriteJsonAtomically(_verificationPath, state);

        private static PersistenceReadResult<T> ReadState<T>(string path, T empty)
        {
            if (!File.Exists(path)) return new(true, empty);
            try
            {
                string json = File.ReadAllText(path);
                T? value = JsonSerializer.Deserialize<T>(json);
                return value is null ? new(false, default) : new(true, value);
            }
            catch { return new(false, default); }
        }

        private static bool WriteJsonAtomically<T>(string path, T state)
        {
            string? temporaryPath = null;
            try
            {
                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                string directory = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(directory, $".maintenance-state.{Guid.NewGuid():N}.tmp");
                using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.WriteThrough))
                using (StreamWriter writer = new(stream))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, path, overwrite: true);
                return true;
            }
            catch { return false; }
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporaryPath))
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
            }
        }

        private sealed record PersistenceReadResult<T>(bool Succeeded, T? Value);
        private sealed record MaintenanceState(DateTimeOffset? LastEvaluationUtc, DateTimeOffset? LastChangeUtc, string LastCategory, string LastSummary)
        { public static MaintenanceState Empty { get; } = new(null, null, string.Empty, string.Empty); }
        private sealed record RuntimeVerificationState(DateTimeOffset? LastVerifiedUtc, bool Passed, string Summary)
        { public static RuntimeVerificationState Empty { get; } = new(null, false, string.Empty); }
    }

    public sealed record IntegratedMaintenanceResult(bool ChangeAttempted, bool Verified, string Category, string Summary)
    {
        public static IntegratedMaintenanceResult NotRun(string summary) => new(false, false, string.Empty, summary);
    }
}
