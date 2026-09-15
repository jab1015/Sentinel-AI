/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
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
        private readonly OptimizationRuntimeStateStore _stateStore;

        public AutomaticOptimizationCoordinator()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Modern Methods", "Sentinel AI");
            Directory.CreateDirectory(directory);
            _stateStore = new OptimizationRuntimeStateStore(Path.Combine(directory, "optimization-runtime-state.json"));
        }

        public async Task<AutomaticOptimizationResult> EvaluateOnlyAsync(SystemSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            cancellationToken.ThrowIfCancellationRequested();

            PerformanceBaselineService.PerformanceBaselineResult baseline = _baselineService.Record(snapshot);
            UnifiedInvestigationAssessment assessment = _assessmentService.Evaluate(snapshot);
            OptimizationSettings settings = _settingsService.Load();
            OptimizationDecision decision = _decisionService.Evaluate(baseline, assessment);
            SubscriptionState subscription = await _subscriptionService.GetStateAsync().ConfigureAwait(false);

            OptimizationSettings scanOnlySettings = settings with { AutomaticOptimizationEnabled = false };
            OptimizationSafetyAssessment safety = _safetyService.Evaluate(decision, scanOnlySettings);

            string summary;
            if (!baseline.IsEstablished)
            {
                summary = $"Manual optimization scan complete. Sentinel is still learning this computer's normal performance baseline ({baseline.SampleCount}/12 checks complete). No changes were made.";
            }
            else if (!decision.OptimizationWarranted)
            {
                summary = "Manual optimization scan complete. Performance is within this computer's established baseline and no verified optimization is needed right now. No changes were made.";
            }
            else if (!subscription.IsActive)
            {
                summary = "Manual optimization scan found a verified optimization opportunity. No changes were made. An active Sentinel subscription is required before Sentinel can apply optimization changes.";
            }
            else
            {
                summary = "Manual optimization scan found a verified optimization opportunity. No changes were made by the scan. Sentinel can apply verified optimizations according to your optimization settings.";
            }

            return new AutomaticOptimizationResult(false, baseline, decision, safety, null, summary);
        }

        public async Task<AutomaticOptimizationResult> EvaluateAndRunAsync(SystemSnapshot snapshot, CancellationToken cancellationToken = default)
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
                if (!_stateStore.TryAcquireExecutionLease(out IDisposable? executionLease) || executionLease is null)
                    return new AutomaticOptimizationResult(false, baseline, decision, safety, null,
                        "Automatic optimization was not run because another Sentinel process may already own the optimization safety lease. No change was attempted.");

                using (executionLease)
                {
                    if (!_stateStore.TryLoad(out OptimizationRuntimeState state))
                        return new AutomaticOptimizationResult(false, baseline, decision, safety, null,
                            "Automatic optimization was not run because Sentinel could not verify its persisted cooldown state. No change was attempted.");

                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    if (state.LastAttemptUtc.HasValue && now - state.LastAttemptUtc.Value < MinimumExecutionInterval)
                        return new AutomaticOptimizationResult(false, baseline, decision, safety, null,
                            "A verified optimization was identified, but Sentinel recently performed or attempted an optimization and is waiting before making another automatic change.");

                    OptimizationRuntimeState reservation = new(now, state.LastSucceededUtc, "Optimization attempt reserved before execution.");
                    if (!_stateStore.TrySave(reservation))
                        return new AutomaticOptimizationResult(false, baseline, decision, safety, null,
                            "Automatic optimization was not run because Sentinel could not durably reserve the attempt. No change was attempted.");

                    OptimizationExecutionResult execution = await _storageExecutor.ExecuteAsync(decision, safety, cancellationToken).ConfigureAwait(false);
                    _outcomeRecorder.Record(execution);

                    OptimizationRuntimeState completed = new(now, execution.Succeeded ? now : state.LastSucceededUtc, execution.Summary);
                    _ = _stateStore.TrySave(completed); // pre-action reservation remains authoritative if this write fails

                    return new AutomaticOptimizationResult(execution.Attempted, baseline, decision, safety, execution, execution.Summary);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new AutomaticOptimizationResult(false, baseline, decision, safety, null,
                    $"Automatic optimization was safely stopped before making a verified change ({ex.GetType().Name}).");
            }
            finally { ExecutionGate.Release(); }
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
