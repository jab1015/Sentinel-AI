/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Product-wide AI coordinator. Applies the same minimal-token policy to
    /// investigations, user communication, and unresolved remediation guidance.
    /// Cloud AI is advisory and is never allowed to bypass Sentinel verification.
    /// </summary>
    public sealed class SmartSentinelAiCoordinator
    {
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
        private readonly AiEscalationPolicy _policy = new();
        private readonly AiEvidencePackageBuilder _packageBuilder = new();
        private readonly CloudAiGatewayClient _gateway = new();
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
        private long _requestsSent;
        private long _inputTokens;
        private long _outputTokens;

        public bool IsCloudConfigured => _gateway.IsConfigured;

        public async Task<SmartAiResult> AnalyzeAsync(
            string purpose,
            string userQuestion,
            SystemSnapshot snapshot,
            ExternalInvestigationResult? external,
            AiEscalationContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(context);

            AiEscalationDecision decision = _policy.Evaluate(context);
            if (!decision.UseCloudAi)
                return SmartAiResult.NotUsed(decision.Reason, decision.ResearchFirst);

            int characterBudget = decision.ModelTier == AiModelTier.Advanced ? 7_000 : 3_500;
            AiEvidencePackage package = _packageBuilder.Build(
                purpose, userQuestion ?? string.Empty, snapshot, external, characterBudget);

            string cacheKey = Hash(package.Payload + "|" + decision.ModelTier);
            if (_cache.TryGetValue(cacheKey, out CacheEntry? entry) && entry.ExpiresUtc > DateTimeOffset.UtcNow)
            {
                return new SmartAiResult(
                    UsedCloudAi: entry.Result.Used,
                    Available: entry.Result.Available,
                    Answer: entry.Result.Answer,
                    ConfidencePercent: entry.Result.ConfidencePercent,
                    RequiresMoreEvidence: entry.Result.RequiresMoreEvidence,
                    FromCache: true,
                    ResearchFirst: false,
                    Provider: entry.Result.Provider,
                    Model: entry.Result.Model,
                    InputTokens: 0,
                    OutputTokens: 0,
                    Reason: "Reused a recent AI analysis for identical redacted evidence; no new token request was sent.");
            }

            CloudAiResult cloud = await _gateway.AnalyzeAsync(package, decision, cancellationToken).ConfigureAwait(false);
            if (cloud.Used)
            {
                Interlocked.Increment(ref _requestsSent);
                Interlocked.Add(ref _inputTokens, cloud.InputTokens);
                Interlocked.Add(ref _outputTokens, cloud.OutputTokens);
                _cache[cacheKey] = new CacheEntry(cloud, DateTimeOffset.UtcNow.Add(CacheLifetime));
            }

            return new SmartAiResult(
                UsedCloudAi: cloud.Used,
                Available: cloud.Available,
                Answer: cloud.Answer,
                ConfidencePercent: cloud.ConfidencePercent,
                RequiresMoreEvidence: cloud.RequiresMoreEvidence,
                FromCache: false,
                ResearchFirst: false,
                Provider: cloud.Provider,
                Model: cloud.Model,
                InputTokens: cloud.InputTokens,
                OutputTokens: cloud.OutputTokens,
                Reason: cloud.Reason);
        }

        public AiUsageSnapshot GetUsage() => new(
            RequestsSent: Interlocked.Read(ref _requestsSent),
            InputTokens: Interlocked.Read(ref _inputTokens),
            OutputTokens: Interlocked.Read(ref _outputTokens),
            CachedAnalyses: _cache.Count,
            CloudConfigured: _gateway.IsConfigured);

        public int RemoveExpiredCacheEntries()
        {
            int removed = 0;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach ((string key, CacheEntry value) in _cache)
            {
                if (value.ExpiresUtc <= now && _cache.TryRemove(key, out _)) removed++;
            }
            return removed;
        }

        private static string Hash(string value)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }

        private sealed record CacheEntry(CloudAiResult Result, DateTimeOffset ExpiresUtc);
    }

    public sealed record SmartAiResult(
        bool UsedCloudAi,
        bool Available,
        string Answer,
        int ConfidencePercent,
        bool RequiresMoreEvidence,
        bool FromCache,
        bool ResearchFirst,
        string Provider,
        string Model,
        int InputTokens,
        int OutputTokens,
        string Reason)
    {
        public static SmartAiResult NotUsed(string reason, bool researchFirst) =>
            new(false, true, string.Empty, 0, false, false, researchFirst,
                string.Empty, string.Empty, 0, 0, reason);
    }

    public sealed record AiUsageSnapshot(
        long RequestsSent,
        long InputTokens,
        long OutputTokens,
        int CachedAnalyses,
        bool CloudConfigured);
}
