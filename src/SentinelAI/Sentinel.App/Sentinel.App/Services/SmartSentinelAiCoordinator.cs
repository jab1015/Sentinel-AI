/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    public sealed class SmartSentinelAiCoordinator
    {
        private const int MaximumCacheEntries = 200;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
        private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);
        private static long _requestsSent;
        private static long _inputTokens;
        private static long _outputTokens;
        private readonly AiEscalationPolicy _policy = new();
        private readonly AiEvidencePackageBuilder _packageBuilder = new();
        private readonly CloudAiGatewayClient _gateway = new();

        public bool IsCloudConfigured => _gateway.IsConfigured;

        public async Task<SmartAiResult> AnalyzeAsync(
            string purpose,
            string userQuestion,
            SystemSnapshot snapshot,
            ExternalInvestigationResult? external,
            AiEscalationContext context,
            CancellationToken cancellationToken = default,
            string? supplementalEvidence = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(context);

            AiEscalationDecision decision = _policy.Evaluate(context);
            if (!decision.UseCloudAi) return SmartAiResult.NotUsed(decision.Reason, decision.ResearchFirst);

            int characterBudget = decision.ModelTier == AiModelTier.Advanced ? 7_000 : 3_500;
            AiEvidencePackage package = _packageBuilder.Build(purpose, userQuestion ?? string.Empty, snapshot, external, characterBudget, supplementalEvidence);

            RemoveExpiredCacheEntries();
            TrimCacheIfNeeded();
            string cacheKey = Hash(CreateStableCachePayload(package.Payload) + "|" + decision.ModelTier);
            if (Cache.TryGetValue(cacheKey, out CacheEntry? entry) && entry.ExpiresUtc > DateTimeOffset.UtcNow)
            {
                return new SmartAiResult(entry.Result.Used, entry.Result.Available, entry.Result.Answer,
                    entry.Result.ConfidencePercent, entry.Result.RequiresMoreEvidence, true, false,
                    entry.Result.Provider, entry.Result.Model, 0, 0,
                    "Reused a recent AI analysis for identical redacted evidence; no new token request was sent.");
            }

            CloudAiResult cloud = await _gateway.AnalyzeAsync(package, decision, cancellationToken).ConfigureAwait(false);
            if (cloud.Used)
            {
                Interlocked.Increment(ref _requestsSent);
                Interlocked.Add(ref _inputTokens, cloud.InputTokens);
                Interlocked.Add(ref _outputTokens, cloud.OutputTokens);
                Cache[cacheKey] = new CacheEntry(cloud, DateTimeOffset.UtcNow.Add(CacheLifetime));
            }

            return new SmartAiResult(cloud.Used, cloud.Available, cloud.Answer, cloud.ConfidencePercent,
                cloud.RequiresMoreEvidence, false, false, cloud.Provider, cloud.Model,
                cloud.InputTokens, cloud.OutputTokens, cloud.Reason);
        }

        public AiUsageSnapshot GetUsage() => new(Interlocked.Read(ref _requestsSent), Interlocked.Read(ref _inputTokens), Interlocked.Read(ref _outputTokens), Cache.Count, _gateway.IsConfigured);

        public int RemoveExpiredCacheEntries()
        {
            int removed = 0;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach ((string key, CacheEntry value) in Cache)
                if (value.ExpiresUtc <= now && Cache.TryRemove(key, out _)) removed++;
            return removed;
        }

        private static string CreateStableCachePayload(string payload)
        {
            string[] lines = payload.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            return string.Join("\n", Array.FindAll(lines,
                line => !line.StartsWith("- snapshot-time:", StringComparison.OrdinalIgnoreCase)));
        }

        private static void TrimCacheIfNeeded()
        {
            if (Cache.Count < MaximumCacheEntries) return;

            foreach (var pair in Cache
                         .OrderBy(item => item.Value.ExpiresUtc)
                         .Take(Math.Max(1, Cache.Count - MaximumCacheEntries + 20)))
            {
                Cache.TryRemove(pair.Key, out _);
            }
        }

        private static string Hash(string value)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }

        private sealed record CacheEntry(CloudAiResult Result, DateTimeOffset ExpiresUtc);
    }

    public sealed record SmartAiResult(bool UsedCloudAi, bool Available, string Answer, int ConfidencePercent,
        bool RequiresMoreEvidence, bool FromCache, bool ResearchFirst, string Provider, string Model,
        int InputTokens, int OutputTokens, string Reason)
    {
        public static SmartAiResult NotUsed(string reason, bool researchFirst) => new(false, true, string.Empty, 0, false, false, researchFirst, string.Empty, string.Empty, 0, 0, reason);
    }

    public sealed record AiUsageSnapshot(long RequestsSent, long InputTokens, long OutputTokens, int CachedAnalyses, bool CloudConfigured);
}
