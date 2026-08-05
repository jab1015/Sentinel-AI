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
    /// Promotes completed verified investigations into reusable knowledge and
    /// invalidates that knowledge whenever material evidence changes.
    /// Knowledge reuse never overrides current critical evidence and never turns
    /// an incomplete investigation into a verified conclusion.
    /// </summary>
    public sealed class TrustedKnowledgeEngine
    {
        private readonly string _storePath;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public TrustedKnowledgeEngine(string? storePath = null)
        {
            _storePath = storePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SentinelAI",
                "TrustedKnowledge",
                "knowledge.json");
        }

        public async Task<KnowledgePromotionResult> PromoteAsync(
            PersistentInvestigationRecord investigation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(investigation);

            if (!CanPromote(investigation, out string rejection))
                return KnowledgePromotionResult.Rejected(rejection);

            TrustedKnowledgeRecord record = new(
                KnowledgeId: Guid.NewGuid(),
                SourceInvestigationId: investigation.InvestigationId,
                KnowledgeKey: CreateKnowledgeKey(investigation.FindingType, investigation.InvalidationState),
                FindingType: investigation.FindingType,
                RootCause: investigation.RootCause,
                Conclusion: investigation.EvidenceSummary,
                ConfidencePercent: Math.Clamp(investigation.ConfidencePercent, 0, 100),
                TrustLevel: investigation.TrustLevel,
                RiskClassification: investigation.RiskClassification,
                VerifiedRepairHistory: investigation.RepairAttempts,
                EvidenceState: investigation.InvalidationState,
                CreatedUtc: DateTimeOffset.UtcNow,
                LastVerifiedUtc: investigation.LastVerifiedUtc,
                ExpiresUtc: CalculateExpiration(investigation),
                IsReusable: true,
                InvalidationReason: string.Empty);

            await UpsertAsync(record, cancellationToken).ConfigureAwait(false);
            return KnowledgePromotionResult.Accepted(record);
        }

        public async Task<KnowledgeReuseResult> FindReusableAsync(
            string findingType,
            InvestigationInvalidationState currentEvidence,
            bool currentConditionCritical,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(currentEvidence);

            if (currentConditionCritical)
                return KnowledgeReuseResult.Rejected("Current critical evidence must be investigated directly and cannot be bypassed by prior knowledge.");

            string key = CreateKnowledgeKey(findingType, currentEvidence);
            IReadOnlyList<TrustedKnowledgeRecord> records = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
            TrustedKnowledgeRecord? candidate = records
                .Where(item => item.IsReusable)
                .Where(item => string.Equals(item.KnowledgeKey, key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.LastVerifiedUtc)
                .FirstOrDefault();

            if (candidate is null)
                return KnowledgeReuseResult.Rejected("No reusable verified knowledge matches this exact evidence state.");

            if (candidate.IsExpired(DateTimeOffset.UtcNow))
            {
                await InvalidateAsync(candidate.KnowledgeKey, "Knowledge expired and requires revalidation.", cancellationToken).ConfigureAwait(false);
                return KnowledgeReuseResult.Rejected("Stored knowledge has expired and must be revalidated.");
            }

            if (PersistentInvestigationMemoryService.HasMaterialChange(candidate.EvidenceState, currentEvidence))
            {
                await InvalidateAsync(candidate.KnowledgeKey, "Material system evidence changed.", cancellationToken).ConfigureAwait(false);
                return KnowledgeReuseResult.Rejected("Material evidence changed, so Sentinel reopened the investigation.");
            }

            return KnowledgeReuseResult.Accepted(candidate);
        }

        public async Task InvalidateAsync(
            string knowledgeKey,
            string reason,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<TrustedKnowledgeRecord> records = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
                for (int i = 0; i < records.Count; i++)
                {
                    if (!string.Equals(records[i].KnowledgeKey, knowledgeKey, StringComparison.OrdinalIgnoreCase))
                        continue;

                    records[i] = records[i] with
                    {
                        IsReusable = false,
                        InvalidationReason = reason?.Trim() ?? "Evidence changed.",
                        LastVerifiedUtc = DateTimeOffset.UtcNow
                    };
                }
                await WriteUnlockedAsync(records, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<IReadOnlyList<TrustedKnowledgeRecord>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public static string CreateKnowledgeKey(string findingType, InvestigationInvalidationState state)
        {
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

        private static bool CanPromote(PersistentInvestigationRecord investigation, out string rejection)
        {
            if (investigation.State == InvestigationLifecycleState.Critical)
            {
                rejection = "Critical investigations remain active evidence and are not promoted as reusable quiet-state knowledge.";
                return false;
            }

            if (investigation.State is InvestigationLifecycleState.InvestigationIncomplete
                or InvestigationLifecycleState.Discovered
                or InvestigationLifecycleState.EvidenceCollected
                or InvestigationLifecycleState.Correlated
                or InvestigationLifecycleState.Investigating
                or InvestigationLifecycleState.RequiresUserApproval
                or InvestigationLifecycleState.RequiresManualRepair)
            {
                rejection = "The investigation is not complete enough to become trusted reusable knowledge.";
                return false;
            }

            if (investigation.ConfidencePercent < 80)
            {
                rejection = "Confidence is below Sentinel's trusted-knowledge threshold.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(investigation.TrustLevel))
            {
                rejection = "A trusted knowledge record requires a verified trust source.";
                return false;
            }

            if (investigation.State == InvestigationLifecycleState.PersistentNoncritical && !investigation.HasExhaustedRepairLedger)
            {
                rejection = "Persistent noncritical knowledge requires an exhausted repair ledger.";
                return false;
            }

            rejection = string.Empty;
            return true;
        }

        private static DateTimeOffset? CalculateExpiration(PersistentInvestigationRecord investigation)
        {
            if (investigation.FindingType.Equals("Driver", StringComparison.OrdinalIgnoreCase))
                return DateTimeOffset.UtcNow.AddDays(30);

            if (investigation.State == InvestigationLifecycleState.Resolved)
                return DateTimeOffset.UtcNow.AddDays(14);

            return DateTimeOffset.UtcNow.AddDays(7);
        }

        private async Task UpsertAsync(TrustedKnowledgeRecord record, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<TrustedKnowledgeRecord> records = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
                int index = records.FindIndex(item => string.Equals(item.KnowledgeKey, record.KnowledgeKey, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    records[index] = record;
                else
                    records.Add(record);
                await WriteUnlockedAsync(records, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<List<TrustedKnowledgeRecord>> ReadUnlockedAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_storePath))
                return new List<TrustedKnowledgeRecord>();

            try
            {
                string json = await File.ReadAllTextAsync(_storePath, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<List<TrustedKnowledgeRecord>>(json, JsonOptions)
                    ?? new List<TrustedKnowledgeRecord>();
            }
            catch (JsonException)
            {
                return new List<TrustedKnowledgeRecord>();
            }
        }

        private async Task WriteUnlockedAsync(IReadOnlyList<TrustedKnowledgeRecord> records, CancellationToken cancellationToken)
        {
            string? directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = _storePath + ".tmp";
            string json = JsonSerializer.Serialize(records, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _storePath, overwrite: true);
        }

        private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();

        public sealed record KnowledgePromotionResult(bool Promoted, string Message, TrustedKnowledgeRecord? Record)
        {
            public static KnowledgePromotionResult Accepted(TrustedKnowledgeRecord record) => new(true, "Verified investigation promoted to trusted knowledge.", record);
            public static KnowledgePromotionResult Rejected(string message) => new(false, message, null);
        }

        public sealed record KnowledgeReuseResult(bool Reused, string Message, TrustedKnowledgeRecord? Record)
        {
            public static KnowledgeReuseResult Accepted(TrustedKnowledgeRecord record) => new(true, "Sentinel reused a still-valid verified conclusion.", record);
            public static KnowledgeReuseResult Rejected(string message) => new(false, message, null);
        }
    }
}
