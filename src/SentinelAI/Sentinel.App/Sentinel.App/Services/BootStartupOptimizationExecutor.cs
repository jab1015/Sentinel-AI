/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Microsoft.Win32;
using System;
using System.IO;
using System.Text.Json;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Executes only a startup-item change approved by the measured boot safety gate.
    /// Automatic execution is intentionally limited to current-user Run entries so
    /// Sentinel can make and roll back the change without altering machine-wide policy.
    /// </summary>
    public sealed class BootStartupOptimizationExecutor
    {
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly BootStartupImpactCorrelationService _correlationService = new();
        private readonly BootStartupOptimizationSafetyService _safetyService = new();

        public BootStartupOptimizationExecutionResult EvaluateAndExecute(
            OptimizationSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            BootStartupImpactCorrelation correlation = _correlationService.Assess();
            BootStartupOptimizationSafetyAssessment safety =
                _safetyService.Evaluate(correlation, settings);

            if (!safety.ExecutionAllowed || safety.ApprovedItem is null)
            {
                return BootStartupOptimizationExecutionResult.Blocked(safety.Summary);
            }

            StartupOptimizationCandidate candidate = safety.ApprovedItem.Candidate;

            // Machine-wide Run entries can be controlled by an administrator,
            // installer, or organizational policy. They are never changed silently.
            if (!candidate.Scope.Equals("Current user", StringComparison.OrdinalIgnoreCase))
            {
                return BootStartupOptimizationExecutionResult.Blocked(
                    "Sentinel verified startup impact, but automatic startup changes are limited to current-user entries. Machine-wide startup configuration was left unchanged.");
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
                if (key is null)
                {
                    return BootStartupOptimizationExecutionResult.Blocked(
                        "The current-user startup registry location is unavailable. No startup change was made.");
                }

                object? currentValue = key.GetValue(candidate.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (currentValue is null)
                {
                    return new BootStartupOptimizationExecutionResult(
                        false,
                        false,
                        true,
                        false,
                        candidate.Name,
                        "The startup item was no longer present when Sentinel rechecked it. No change was needed.");
                }

                string currentCommand = currentValue.ToString() ?? string.Empty;
                if (!CommandsEquivalent(currentCommand, candidate.Command))
                {
                    return BootStartupOptimizationExecutionResult.Blocked(
                        "The startup item changed after Sentinel measured it. Sentinel refused to modify stale evidence.");
                }

                RegistryValueKind valueKind = key.GetValueKind(candidate.Name);
                StartupRollbackRecord rollbackRecord = new(
                    candidate.Name,
                    currentCommand,
                    valueKind,
                    DateTimeOffset.UtcNow);

                if (!TryPersistRollbackRecord(rollbackRecord))
                {
                    return BootStartupOptimizationExecutionResult.Blocked(
                        "Sentinel could not preserve rollback information, so the startup item was not changed.");
                }

                key.DeleteValue(candidate.Name, throwOnMissingValue: false);

                bool removed = key.GetValue(candidate.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is null;
                if (!removed)
                {
                    bool rolledBack = Restore(key, rollbackRecord);
                    return new BootStartupOptimizationExecutionResult(
                        true,
                        false,
                        false,
                        rolledBack,
                        candidate.Name,
                        rolledBack
                            ? "Windows did not accept the startup optimization. Sentinel restored the original startup entry."
                            : "Windows did not accept the startup optimization and Sentinel could not verify rollback. No further startup changes will be attempted automatically.");
                }

                return new BootStartupOptimizationExecutionResult(
                    true,
                    true,
                    true,
                    false,
                    candidate.Name,
                    "Sentinel disabled one verified high-impact current-user startup item and preserved the original value for rollback. Boot improvement will be evaluated after future restarts.");
            }
            catch (Exception ex)
            {
                return new BootStartupOptimizationExecutionResult(
                    true,
                    false,
                    false,
                    false,
                    candidate.Name,
                    $"Sentinel could not safely complete the startup optimization: {ex.Message}");
            }
        }

        private static bool CommandsEquivalent(string left, string right) =>
            string.Equals(
                Environment.ExpandEnvironmentVariables(left).Trim(),
                Environment.ExpandEnvironmentVariables(right).Trim(),
                StringComparison.OrdinalIgnoreCase);

        private static bool TryPersistRollbackRecord(StartupRollbackRecord record)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Modern Methods",
                    "Sentinel AI",
                    "rollback");

                Directory.CreateDirectory(directory);
                string safeName = string.Join("_", record.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                if (string.IsNullOrWhiteSpace(safeName))
                    safeName = "startup-item";

                string path = Path.Combine(directory, $"startup-{safeName}.json");
                File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool Restore(RegistryKey key, StartupRollbackRecord record)
        {
            try
            {
                key.SetValue(record.Name, record.Command, record.ValueKind);
                object? restored = key.GetValue(record.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return restored is not null &&
                    CommandsEquivalent(restored.ToString() ?? string.Empty, record.Command);
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed record StartupRollbackRecord(
        string Name,
        string Command,
        RegistryValueKind ValueKind,
        DateTimeOffset CreatedUtc);

    public sealed record BootStartupOptimizationExecutionResult(
        bool Attempted,
        bool Changed,
        bool Verified,
        bool RolledBack,
        string StartupItemName,
        string Summary)
    {
        public static BootStartupOptimizationExecutionResult Blocked(string summary) =>
            new(false, false, false, false, string.Empty, summary);
    }
}
