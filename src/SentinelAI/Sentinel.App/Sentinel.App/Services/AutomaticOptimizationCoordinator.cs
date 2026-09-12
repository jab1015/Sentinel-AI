/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Production coordinator for Sentinel's automatic optimization pipeline.
    /// It learns the local baseline, obtains one authoritative health assessment,
    /// evaluates optimization candidates, applies the user's safety policy, and
    /// invokes only a verified executor. Attempts are durably reserved before an
    /// executor is invoked so persistence failure cannot bypass the cooldown.
    /// </summary>
    public sealed class AutomaticOptimizationCoordinator
    {
        private static readonly TimeSpan MinimumExecutionInterval = TimeSpan.FromHours(12);
        private static readonly SemaphoreSlim ExecutionGate = new(1, 1);

        private readonly PerformanceBaselineService _baselineService = new();
        private readonly UnifiedInvestigationAssessmentService _assessmentService = new();
        private readonly OptimizationDecisionService _decisionService = new();
        private readonly OptimizationSafetyService _safetyService = new();
        private readonly OptimizationSettingsService _settingsService = new();
        private readonly StoreSubscriptionService _subscriptionService = new();
        private readonly SafeTemporaryStorageOptimizationExecutor _storageExecutor = new();
        private readonly MaintenanceOutcomeRecorder _outcomeRecorder = new();
        private readonly string _statePath;

        public AutomaticOptimizationCoordinator()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Modern Methods",
                "Sentinel AI");

            Directory.CreateDirectory(directory);
            _statePath = Path.Combine(directory, "optimization-runtime-state.json");
        }

        public async Task<AutomaticOptimizationResult> EvaluateAndRunAsync(
            SystemSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            PerformanceBaselineService.PerformanceBaselineResult baseline = _baselineService.Record(snapshot);
            UnifiedInvestigationAssessment assessment = _assessmentService.Evaluate(snapshot);
            OptimizationSettings settings = _settingsService.Load();
            OptimizationDecision decision = _decisionService.Evaluate(baseline, assessment);

            SubscriptionState subscription = await _subscriptionService.GetStateAsync().ConfigureAwait(false);
            if (!subscription.IsActive)
            {
                OptimizationSettings monitoringOnlySettings = settings with { AutomaticOptimizationEnabled = false };
                OptimizationSafetyAssessment monitoringOnlySafety = _safetyService.Evaluate(decision, monitoringOnlySettings);
                return new AutomaticOptimizationResult(false, baseline, decision, monitoringOnlySafety, null,
                    "Sentinel completed free local performance monitoring. An active subscription is required before Sentinel can apply optimization changes.");
            }

            OptimizationSafetyAssessment safety = _safetyService.Evaluate(decision, settings);
            if (!safety.ExecutionAllowed)
                return new AutomaticOptimizationResult(false, baseline, decision, safety, null, safety.Summary);

            await ExecutionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!TryLoadState(out RuntimeState state))
                {
                    return new AutomaticOptimizationResult(false, baseline, decision, safety, null,
                        "Automatic optimization was not run because Sentinel could not verify its persisted cooldown state. No change was attempted.");
                }

                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (state.LastAttemptUtc.HasValue && now - state.LastAttemptUtc.Value < MinimumExecutionInterval)
                {
                    return new AutomaticOptimizationResult(false, baseline, decision, safety, null,
                        "A verified optimization was identified, but Sentinel recently performed or attempted an optimization and is waiting before making another automatic change.");
                }

                RuntimeState reservation = new(now, state.LastSucceededUtc, "Optimization attempt reserved before execution.");
                if (!TrySaveState(reservation))
                {
                    return new AutomaticOptimizationResult(false, baseline, decision, safety, null,
                        "Automatic optimization was not run because Sentinel could not durably reserve the attempt. No change was attempted.");
                }

                OptimizationExecutionResult execution = await _storageExecutor.ExecuteAsync(decision, safety, cancellationToken).ConfigureAwait(false);
                _outcomeRecorder.Record(execution);

                RuntimeState completed = new(
                    LastAttemptUtc: now,
                    LastSucceededUtc: execution.Succeeded ? now : state.LastSucceededUtc,
                    LastSummary: execution.Summary);

                // The pre-action reservation is already durable. A post-action write failure
                // must never erase that reservation or permit an immediate repeated action.
                _ = TrySaveState(completed);

                return new AutomaticOptimizationResult(execution.Attempted, baseline, decision, safety, execution, execution.Summary);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new AutomaticOptimizationResult(false, baseline, decision, safety, null,
                    $"Automatic optimization was safely stopped before making a verified change ({ex.GetType().Name}).");
            }
            finally
            {
                ExecutionGate.Release();
            }
        }

        private bool TryLoadState(out RuntimeState state)
        {
            state = RuntimeState.Empty;
            try
            {
                if (!File.Exists(_statePath))
                    return true;

                string json = File.ReadAllText(_statePath);
                RuntimeState? loaded = JsonSerializer.Deserialize<RuntimeState>(json);
                if (loaded is null)
                    return false;

                state = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TrySaveState(RuntimeState state)
        {
            string? temporaryPath = null;
            try
            {
                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                string directory = Path.GetDirectoryName(_statePath)!;
                temporaryPath = Path.Combine(directory, $".optimization-runtime-state.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, _statePath, overwrite: true);

                string persisted = File.ReadAllText(_statePath);
                RuntimeState? verified = JsonSerializer.Deserialize<RuntimeState>(persisted);
                return verified is not null && verified.LastAttemptUtc == state.LastAttemptUtc;
            }
            catch
            {
                return false;
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

        private sealed record RuntimeState(DateTimeOffset? LastAttemptUtc, DateTimeOffset? LastSucceededUtc, string LastSummary)
        {
            public static RuntimeState Empty { get; } = new(null, null, string.Empty);
        }
    }

    public sealed record AutomaticOptimizationResult(
        bool ExecutionAttempted,
        PerformanceBaselineService.PerformanceBaselineResult Baseline,
        OptimizationDecision Decision,
        OptimizationSafetyAssessment Safety,
        OptimizationExecutionResult? Execution,
        string Summary);
}
