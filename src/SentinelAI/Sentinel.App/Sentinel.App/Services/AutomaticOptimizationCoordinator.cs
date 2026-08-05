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

        private readonly PerformanceBaselineService _baselineService = new();
        private readonly UnifiedInvestigationAssessmentService _assessmentService = new();
        private readonly OptimizationDecisionService _decisionService = new();
        private readonly OptimizationSafetyService _safetyService = new();
        private readonly OptimizationSettingsService _settingsService = new();
        private readonly SafeTemporaryStorageOptimizationExecutor _storageExecutor = new();
        private readonly MaintenanceOutcomeRecorder _outcomeRecorder = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
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

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                _gate.Release();
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
