/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Client for the Modern Methods server-side AI gateway.
    /// No provider API secret is stored in Sentinel. The production HTTPS endpoint
    /// is built in, while SENTINEL_AI_GATEWAY_URL can override it for testing.
    ///
    /// RELEASE SAFETY BOUNDARY:
    /// A current Microsoft Store subscription must be verified before any request
    /// that can consume paid cloud-AI tokens is transmitted.
    /// </summary>
    public sealed class CloudAiGatewayClient
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SubscriptionCacheLifetime = TimeSpan.FromMinutes(5);
        private const string ProductionEndpoint =
            "https://sentinel-ai-gateway-49908265995.us-central1.run.app/v1/analyze";

        private static readonly SemaphoreSlim SubscriptionLock = new(1, 1);
        private static SubscriptionState? _cachedSubscriptionState;
        private static DateTimeOffset _cachedSubscriptionAt;

        private readonly HttpClient _httpClient;
        private readonly Uri? _endpoint;
        private readonly StoreSubscriptionService _subscriptionService = new();

        public CloudAiGatewayClient()
        {
            _httpClient = new HttpClient
            {
                Timeout = RequestTimeout,
                MaxResponseContentBufferSize = 256 * 1024
            };

            string endpoint = ProductionEndpoint;
#if DEBUG
            string? configured = Environment.GetEnvironmentVariable("SENTINEL_AI_GATEWAY_URL");
            if (!string.IsNullOrWhiteSpace(configured))
                endpoint = configured.Trim();
#endif

            if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
                _endpoint = uri;
        }

        public bool IsConfigured => _endpoint is not null;

        public async Task<CloudAiResult> AnalyzeAsync(
            AiEvidencePackage evidence,
            AiEscalationDecision decision,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            ArgumentNullException.ThrowIfNull(decision);

            if (!decision.UseCloudAi)
                return CloudAiResult.NotUsed(decision.Reason);

            if (_endpoint is null)
                return CloudAiResult.Unavailable("Secure AI gateway is not configured. Sentinel will continue using local and authoritative research only.");

            if (evidence.EstimatedInputTokens > decision.MaximumTotalTokens)
                return CloudAiResult.Unavailable("The evidence package exceeds Sentinel's AI token budget, so no cloud request was sent.");

            // This check intentionally happens immediately before constructing/sending
            // the paid request. If Store licensing cannot be positively verified,
            // Sentinel fails closed and continues with free local functionality.
            SubscriptionState subscription = await GetVerifiedSubscriptionStateAsync(cancellationToken).ConfigureAwait(false);
            if (!subscription.IsActive)
            {
                return CloudAiResult.SubscriptionRequired(
                    string.IsNullOrWhiteSpace(subscription.Summary)
                        ? "A Sentinel AI subscription is required for cloud AI investigations. Local monitoring remains available."
                        : subscription.Summary);
            }

            CloudAiRequest request = new(
                SchemaVersion: 1,
                Purpose: evidence.Purpose,
                ModelTier: decision.ModelTier.ToString(),
                MaximumTotalTokens: decision.MaximumTotalTokens,
                Evidence: evidence.Payload);

            try
            {
                using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(_endpoint, request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return CloudAiResult.Unavailable($"Secure AI gateway returned HTTP {(int)response.StatusCode}. Sentinel did not rely on a cloud answer.");

                CloudAiResponse? body = await response.Content.ReadFromJsonAsync<CloudAiResponse>(
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken).ConfigureAwait(false);

                if (body is null || string.IsNullOrWhiteSpace(body.Answer))
                    return CloudAiResult.Unavailable("Secure AI gateway returned no usable answer.");

                return new CloudAiResult(
                    Used: true,
                    Available: true,
                    Answer: Limit(body.Answer.Trim(), 8_000),
                    Provider: Limit(body.Provider?.Trim() ?? string.Empty, 100),
                    Model: Limit(body.Model?.Trim() ?? string.Empty, 100),
                    InputTokens: Math.Max(0, body.InputTokens),
                    OutputTokens: Math.Max(0, body.OutputTokens),
                    ConfidencePercent: Math.Clamp(body.ConfidencePercent, 0, 100),
                    RequiresMoreEvidence: body.RequiresMoreEvidence,
                    Reason: "Secure AI gateway returned an advisory analysis. Sentinel must still validate any actionable conclusion against local evidence.",
                    RequiresSubscription: false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return CloudAiResult.Unavailable("Secure AI gateway timed out. Sentinel continued without cloud AI.");
            }
            catch
            {
                return CloudAiResult.Unavailable("Secure AI gateway is temporarily unavailable. Sentinel continued without cloud AI.");
            }
        }

        private static string Limit(string value, int maximum) =>
            value.Length <= maximum ? value : value[..maximum];

        public static void InvalidateSubscriptionCache()
        {
            _cachedSubscriptionState = null;
            _cachedSubscriptionAt = default;
        }

        private async Task<SubscriptionState> GetVerifiedSubscriptionStateAsync(CancellationToken cancellationToken)
        {
#if DEBUG
            return await _subscriptionService.GetStateAsync().ConfigureAwait(false);
#else
            DateTimeOffset now = DateTimeOffset.UtcNow;
            SubscriptionState? cached = _cachedSubscriptionState;
            if (cached is not null && now - _cachedSubscriptionAt < SubscriptionCacheLifetime)
                return cached;

            await SubscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                now = DateTimeOffset.UtcNow;
                cached = _cachedSubscriptionState;
                if (cached is not null && now - _cachedSubscriptionAt < SubscriptionCacheLifetime)
                    return cached;

                SubscriptionState current = await _subscriptionService.GetStateAsync().ConfigureAwait(false);
                _cachedSubscriptionState = current;
                _cachedSubscriptionAt = now;
                return current;
            }
            finally
            {
                SubscriptionLock.Release();
            }
#endif
        }

        private sealed record CloudAiRequest(
            int SchemaVersion,
            string Purpose,
            string ModelTier,
            int MaximumTotalTokens,
            string Evidence);

        private sealed record CloudAiResponse(
            string? Answer,
            string? Provider,
            string? Model,
            int InputTokens,
            int OutputTokens,
            int ConfidencePercent,
            bool RequiresMoreEvidence);
    }

    public sealed record CloudAiResult(
        bool Used,
        bool Available,
        string Answer,
        string Provider,
        string Model,
        int InputTokens,
        int OutputTokens,
        int ConfidencePercent,
        bool RequiresMoreEvidence,
        string Reason,
        bool RequiresSubscription = false)
    {
        public static CloudAiResult NotUsed(string reason) =>
            new(false, true, string.Empty, string.Empty, string.Empty, 0, 0, 0, false, reason);

        public static CloudAiResult Unavailable(string reason) =>
            new(false, false, string.Empty, string.Empty, string.Empty, 0, 0, 0, false, reason);

        public static CloudAiResult SubscriptionRequired(string reason) =>
            new(false, false, string.Empty, string.Empty, string.Empty, 0, 0, 0, false, reason, true);
    }
}
