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
    /// invokes only a verified executor. Successful or attempted actions are
    /// rate-limited across application restarts.
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

            PerformanceBaselineService.PerformanceBaselineResult baseline =
                _baselineService.Record(snapshot);

            UnifiedInvestigationAssessment assessment =
                _assessmentService.Evaluate(snapshot);

            OptimizationSettings settings = _settingsService.Load();
            OptimizationDecision decision = _decisionService.Evaluate(baseline, assessment);

            // Baseline learning and local assessment remain free. Any optimization
            // execution requires a positively verified entitlement at the execution
            // boundary, so stale UI state cannot bypass licensing.
            SubscriptionState subscription = await _subscriptionService.GetStateAsync().ConfigureAwait(false);
            if (!subscription.IsActive)
            {
                OptimizationSettings monitoringOnlySettings = settings with
                {
                    AutomaticOptimizationEnabled = false
                };
                OptimizationSafetyAssessment monitoringOnlySafety =
                    _safetyService.Evaluate(decision, monitoringOnlySettings);

                return new AutomaticOptimizationResult(
                    false,
                    baseline,
                    decision,
                    monitoringOnlySafety,
                    null,
                    "Sentinel completed free local performance monitoring. An active subscription is required before Sentinel can apply optimization changes.");
            }

            OptimizationSafetyAssessment safety = _safetyService.Evaluate(decision, settings);

            if (!safety.ExecutionAllowed)
            {
                return new AutomaticOptimizationResult(
                    false,
                    baseline,
                    decision,
                    safety,
                    null,
                    safety.Summary);
            }

            await ExecutionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RuntimeState state = LoadState();
                DateTimeOffset now = DateTimeOffset.UtcNow;

                if (state.LastAttemptUtc.HasValue &&
                    now - state.LastAttemptUtc.Value < MinimumExecutionInterval)
                {
                    return new AutomaticOptimizationResult(
                        false,
                        baseline,
                        decision,
                        safety,
                        null,
                        "A verified optimization was identified, but Sentinel recently performed an optimization and is waiting before making another automatic change.");
                }

                OptimizationExecutionResult execution =
                    await _storageExecutor.ExecuteAsync(decision, safety, cancellationToken)
                        .ConfigureAwait(false);
                _outcomeRecorder.Record(execution);

                if (execution.Attempted)
                {
                    SaveState(new RuntimeState(
                        LastAttemptUtc: now,
                        LastSucceededUtc: execution.Succeeded ? now : state.LastSucceededUtc,
                        LastSummary: execution.Summary));
                }

                return new AutomaticOptimizationResult(
                    execution.Attempted,
                    baseline,
                    decision,
                    safety,
                    execution,
                    execution.Summary);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new AutomaticOptimizationResult(
                    false,
                    baseline,
                    decision,
                    safety,
                    null,
                    $"Automatic optimization was safely stopped before making a verified change ({ex.GetType().Name}).");
            }
            finally
            {
                ExecutionGate.Release();
            }
        }

        private RuntimeState LoadState()
        {
            try
            {
                if (!File.Exists(_statePath))
                    return RuntimeState.Empty;

                string json = File.ReadAllText(_statePath);
                return JsonSerializer.Deserialize<RuntimeState>(json) ?? RuntimeState.Empty;
            }
            catch
            {
                return RuntimeState.Empty;
            }
        }

        private void SaveState(RuntimeState state)
        {
            string? temporaryPath = null;
            try
            {
                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                string directory = Path.GetDirectoryName(_statePath)!;
                temporaryPath = Path.Combine(
                    directory,
                    $".optimization-runtime-state.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, _statePath, overwrite: true);
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

        private sealed record RuntimeState(
            DateTimeOffset? LastAttemptUtc,
            DateTimeOffset? LastSucceededUtc,
            string LastSummary)
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
