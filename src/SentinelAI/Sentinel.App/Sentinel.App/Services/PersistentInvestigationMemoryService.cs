/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Persists verified investigation conclusions and determines whether a stored
    /// conclusion can be safely reused for the current evidence fingerprint.
    /// Notification suppression never disables monitoring and is permitted only
    /// after the record satisfies the exhaustive-remediation and noncritical gate.
    /// </summary>
    public sealed class PersistentInvestigationMemoryService
    {
        private readonly string _storePath;
        private static readonly SemaphoreSlim StoreGate = new(1, 1);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public PersistentInvestigationMemoryService(string? storePath = null)
        {
            _storePath = Path.GetFullPath(storePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SentinelAI",
                "InvestigationMemory",
                "investigations.json"));
        }

        public static string CreateFingerprint(
            string findingType,
            InvestigationInvalidationState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            string normalized = string.Join("|",
                Normalize(findingType),
                Normalize(state.DeviceInstanceId),
                Normalize(state.HardwareId),
                Normalize(state.ErrorCode),
                Normalize(state.DriverVersion),
                Normalize(state.WindowsBuild),
                Normalize(state.BiosVersion),
                Normalize(state.Manufacturer),
                Normalize(state.Model));

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public async Task<PersistentInvestigationRecord?> FindReusableAsync(
            string fingerprint,
            InvestigationInvalidationState currentState,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
                return null;

            IReadOnlyList<PersistentInvestigationRecord> records =
                await ReadAllAsync(cancellationToken).ConfigureAwait(false);

            PersistentInvestigationRecord? record = records
                .Where(item => string.Equals(item.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.LastVerifiedUtc)
                .FirstOrDefault();

            if (record is null || HasMaterialChange(record.InvalidationState, currentState))
                return null;

            return record;
        }

        public async Task UpsertAsync(
            PersistentInvestigationRecord record,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (string.IsNullOrWhiteSpace(record.Fingerprint))
                throw new ArgumentException("A persistent investigation requires a fingerprint.", nameof(record));

            await StoreGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<PersistentInvestigationRecord> records = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
                int index = records.FindIndex(item =>
                    string.Equals(item.Fingerprint, record.Fingerprint, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                    records[index] = record;
                else
                    records.Add(record);

                await WriteUnlockedAsync(records, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                StoreGate.Release();
            }
        }

        public async Task<SuppressionDecision> SetSilentMonitoringAsync(
            string fingerprint,
            bool suppress,
            string reason,
            CancellationToken cancellationToken = default)
        {
            await StoreGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<PersistentInvestigationRecord> records = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
                int index = records.FindIndex(item =>
                    string.Equals(item.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));

                if (index < 0)
                    return SuppressionDecision.Rejected("Sentinel could not find a completed investigation for this exact condition.");

                PersistentInvestigationRecord current = records[index];
                if (suppress && !current.IsEligibleForSilentMonitoring)
                {
                    return SuppressionDecision.Rejected(
                        "Sentinel cannot silence this condition until the verified investigation is complete, all applicable safe repair paths are exhausted, and the remaining condition is classified as noncritical.");
                }

                PersistentInvestigationRecord updated = current with
                {
                    NotificationsSuppressed = suppress,
                    SuppressedAtUtc = suppress ? DateTimeOffset.UtcNow : null,
                    SuppressionReason = suppress ? (reason?.Trim() ?? string.Empty) : string.Empty,
                    LastVerifiedUtc = DateTimeOffset.UtcNow
                };

                records[index] = updated;
                await WriteUnlockedAsync(records, cancellationToken).ConfigureAwait(false);

                return SuppressionDecision.Accepted(
                    updated,
                    suppress
                        ? "Sentinel will continue monitoring this exact condition silently and will reactivate it if material evidence changes."
                        : "Sentinel will resume notifications for this condition.");
            }
            finally
            {
                StoreGate.Release();
            }
        }

        public async Task<IReadOnlyList<PersistentInvestigationRecord>> ReadAllAsync(
            CancellationToken cancellationToken = default)
        {
            await StoreGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                StoreGate.Release();
            }
        }

        public static bool HasMaterialChange(
            InvestigationInvalidationState stored,
            InvestigationInvalidationState current)
        {
            ArgumentNullException.ThrowIfNull(stored);
            ArgumentNullException.ThrowIfNull(current);

            return Different(stored.DeviceInstanceId, current.DeviceInstanceId) ||
                   Different(stored.HardwareId, current.HardwareId) ||
                   Different(stored.ErrorCode, current.ErrorCode) ||
                   Different(stored.DriverVersion, current.DriverVersion) ||
                   Different(stored.WindowsBuild, current.WindowsBuild) ||
                   Different(stored.BiosVersion, current.BiosVersion) ||
                   Different(stored.Manufacturer, current.Manufacturer) ||
                   Different(stored.Model, current.Model) ||
                   Different(stored.Severity, current.Severity) ||
                   Different(stored.VerifiedRepairSignature, current.VerifiedRepairSignature);
        }

        private async Task<List<PersistentInvestigationRecord>> ReadUnlockedAsync(
            CancellationToken cancellationToken)
        {
            if (!File.Exists(_storePath))
                return new List<PersistentInvestigationRecord>();

            try
            {
                string json = await File.ReadAllTextAsync(_storePath, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<List<PersistentInvestigationRecord>>(json, JsonOptions)
                    ?? new List<PersistentInvestigationRecord>();
            }
            catch (JsonException)
            {
                return new List<PersistentInvestigationRecord>();
            }
        }

        private async Task WriteUnlockedAsync(
            IReadOnlyList<PersistentInvestigationRecord> records,
            CancellationToken cancellationToken)
        {
            string? directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string json = JsonSerializer.Serialize(records, JsonOptions);
                await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, _storePath, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // Best-effort cleanup of this exact temporary file only.
                }
            }
        }

        private static string Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? "unknown"
                : value.Trim().ToLowerInvariant();

        private static bool Different(string? left, string? right) =>
            !string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

        public sealed record SuppressionDecision(
            bool Allowed,
            string Message,
            PersistentInvestigationRecord? Record)
        {
            public static SuppressionDecision Rejected(string message) => new(false, message, null);

            public static SuppressionDecision Accepted(
                PersistentInvestigationRecord record,
                string message) => new(true, message, record);
        }
    }
}
