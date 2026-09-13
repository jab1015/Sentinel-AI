/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Client for the Modern Methods server-side AI gateway.
    /// Provider credentials and authoritative paid-entitlement decisions remain server-side.
    /// The client obtains only short-lived gateway sessions.
    /// </summary>
    public sealed class CloudAiGatewayClient
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SessionRefreshCeiling = TimeSpan.FromMinutes(8);
        private static readonly TimeSpan SessionExpirySafetyMargin = TimeSpan.FromSeconds(30);
        private const string ProductionEndpoint =
            "https://sentinel-ai-gateway-49908265995.us-central1.run.app/v1/analyze";

        private static readonly SemaphoreSlim SessionLock = new(1, 1);
        private static GatewaySession? _basicSession;
        private static GatewaySession? _advancedSession;

        private readonly HttpClient _httpClient;
        private readonly Uri? _endpoint;
        private readonly Uri? _gatewayRoot;
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
            {
                _endpoint = uri;
                _gatewayRoot = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
            }
        }

        public bool IsConfigured => _endpoint is not null && _gatewayRoot is not null;

        public async Task<CloudAiResult> AnalyzeAsync(
            AiEvidencePackage evidence,
            AiEscalationDecision decision,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            ArgumentNullException.ThrowIfNull(decision);

            if (!decision.UseCloudAi)
                return CloudAiResult.NotUsed(decision.Reason);

            if (_endpoint is null || _gatewayRoot is null)
                return CloudAiResult.Unavailable("Secure AI gateway is not configured. Sentinel will continue using local and authoritative research only.");

            if (evidence.EstimatedInputTokens > decision.MaximumTotalTokens)
                return CloudAiResult.Unavailable("The evidence package exceeds Sentinel's AI token budget, so no cloud request was sent.");

            bool wantsAdvanced = decision.ModelTier.ToString().Equals("Advanced", StringComparison.OrdinalIgnoreCase);

            try
            {
                // Session bootstrap is part of the same untrusted network boundary as /v1/analyze.
                // Timeouts, malformed/oversized JSON, transport failures, or Store bootstrap faults
                // must fail closed to local analysis rather than escape into the Ask Sentinel UI.
                GatewaySessionResult sessionResult = await GetSessionAsync(wantsAdvanced, cancellationToken).ConfigureAwait(false);
                if (!sessionResult.Succeeded || sessionResult.Session is null)
                {
                    return sessionResult.RequiresSubscription
                        ? CloudAiResult.SubscriptionRequired(sessionResult.Reason)
                        : CloudAiResult.Unavailable(sessionResult.Reason);
                }

                string requestId = Guid.NewGuid().ToString();
                CloudAiRequest request = new(
                    SchemaVersion: 1,
                    RequestId: requestId,
                    Purpose: evidence.Purpose,
                    ModelTier: wantsAdvanced ? "Advanced" : "Basic",
                    MaximumTotalTokens: decision.MaximumTotalTokens,
                    Evidence: evidence.Payload);

                using HttpRequestMessage message = new(HttpMethod.Post, _endpoint)
                {
                    Content = JsonContent.Create(request)
                };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionResult.Session.AccessToken);

                using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    InvalidateSession(wantsAdvanced);
                    return wantsAdvanced
                        ? CloudAiResult.SubscriptionRequired("The secure gateway could not verify an active paid entitlement. Free local monitoring and Basic Ask Sentinel remain available.")
                        : CloudAiResult.Unavailable("The secure gateway rejected the current AI session. Sentinel continued without cloud AI.");
                }

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
            catch (HttpRequestException)
            {
                return CloudAiResult.Unavailable("Secure AI gateway is temporarily unavailable. Sentinel continued without cloud AI.");
            }
            catch (JsonException)
            {
                return CloudAiResult.Unavailable("Secure AI gateway returned invalid data. Sentinel continued without cloud AI.");
            }
            catch (InvalidOperationException)
            {
                return CloudAiResult.Unavailable("Secure AI gateway session setup could not be completed safely. Sentinel continued without cloud AI.");
            }
        }

        public static void InvalidateSubscriptionCache()
        {
            _basicSession = null;
            _advancedSession = null;
        }

        private async Task<GatewaySessionResult> GetSessionAsync(bool advanced, CancellationToken cancellationToken)
        {
            GatewaySession? cached = advanced ? _advancedSession : _basicSession;
            if (cached is not null && CanReuseSession(cached))
                return GatewaySessionResult.Success(cached);

            await SessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cached = advanced ? _advancedSession : _basicSession;
                if (cached is not null && CanReuseSession(cached))
                    return GatewaySessionResult.Success(cached);

                GatewaySessionResult result = advanced
                    ? await CreateStoreSessionAsync(cancellationToken).ConfigureAwait(false)
                    : await CreateFreeSessionAsync(cancellationToken).ConfigureAwait(false);

                if (result.Succeeded && result.Session is not null)
                {
                    if (advanced) _advancedSession = result.Session;
                    else _basicSession = result.Session;
                }

                return result;
            }
            finally
            {
                SessionLock.Release();
            }
        }

        private async Task<GatewaySessionResult> CreateFreeSessionAsync(CancellationToken cancellationToken)
        {
            if (_gatewayRoot is null) return GatewaySessionResult.Unavailable("Secure AI gateway is not configured.");
            Uri uri = new(_gatewayRoot, "v1/session/free");
            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(uri, new { schemaVersion = 1 }, cancellationToken).ConfigureAwait(false);
            return await ReadSessionResponseAsync(response, requiresSubscription: false, expectedTier: "Basic", cancellationToken).ConfigureAwait(false);
        }

        private async Task<GatewaySessionResult> CreateStoreSessionAsync(CancellationToken cancellationToken)
        {
            if (_gatewayRoot is null) return GatewaySessionResult.Unavailable("Secure AI gateway is not configured.");

            Uri ticketUri = new(_gatewayRoot, "v1/store/collections-ticket");
            using HttpResponseMessage ticketResponse = await _httpClient.GetAsync(ticketUri, cancellationToken).ConfigureAwait(false);
            if (!ticketResponse.IsSuccessStatusCode)
                return GatewaySessionResult.Unavailable("The secure gateway could not start Microsoft Store entitlement verification.");

            StoreCollectionsTicketResponse? ticket = await ticketResponse.Content.ReadFromJsonAsync<StoreCollectionsTicketResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken).ConfigureAwait(false);
            if (ticket is null || string.IsNullOrWhiteSpace(ticket.ServiceTicket) || string.IsNullOrWhiteSpace(ticket.PublisherUserId))
                return GatewaySessionResult.Unavailable("The secure gateway returned an invalid Microsoft Store entitlement ticket.");

            StoreCollectionsIdentityResult collections = await _subscriptionService.CreateCollectionsIdentityAsync(
                ticket.ServiceTicket,
                ticket.PublisherUserId).ConfigureAwait(false);
            if (!collections.Succeeded)
                return GatewaySessionResult.SubscriptionRequired(
                    string.IsNullOrWhiteSpace(collections.Message)
                        ? "Microsoft Store could not verify this Sentinel subscription."
                        : collections.Message);

            Uri sessionUri = new(_gatewayRoot, "v1/session/store");
            using HttpResponseMessage sessionResponse = await _httpClient.PostAsJsonAsync(
                sessionUri,
                new StoreSessionRequest(1, collections.CollectionsId),
                cancellationToken).ConfigureAwait(false);

            if (sessionResponse.StatusCode == HttpStatusCode.Forbidden)
                return GatewaySessionResult.SubscriptionRequired("Microsoft Store did not report an active paid Sentinel entitlement.");

            return await ReadSessionResponseAsync(sessionResponse, requiresSubscription: true, expectedTier: "Advanced", cancellationToken).ConfigureAwait(false);
        }

        private static async Task<GatewaySessionResult> ReadSessionResponseAsync(
            HttpResponseMessage response,
            bool requiresSubscription,
            string expectedTier,
            CancellationToken cancellationToken)
        {
            if (!response.IsSuccessStatusCode)
            {
                string reason = $"Secure gateway session setup failed with HTTP {(int)response.StatusCode}.";
                return requiresSubscription
                    ? GatewaySessionResult.SubscriptionRequired(reason)
                    : GatewaySessionResult.Unavailable(reason);
            }

            GatewaySessionResponse? body = await response.Content.ReadFromJsonAsync<GatewaySessionResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken).ConfigureAwait(false);
            if (body is null || string.IsNullOrWhiteSpace(body.AccessToken) || body.AccessToken.Length > 16_384 ||
                body.ExpiresInSeconds <= 0 || !string.Equals(body.Tier, expectedTier, StringComparison.OrdinalIgnoreCase))
                return GatewaySessionResult.Unavailable("Secure gateway returned an invalid session.");

            return GatewaySessionResult.Success(new GatewaySession(
                body.AccessToken,
                expectedTier,
                DateTimeOffset.UtcNow,
                body.ExpiresInSeconds));
        }

        private static bool CanReuseSession(GatewaySession session)
        {
            if (session.ExpiresInSeconds <= 0)
                return false;

            TimeSpan serverLifetime = TimeSpan.FromSeconds(session.ExpiresInSeconds);
            TimeSpan refreshAge = serverLifetime > SessionExpirySafetyMargin
                ? serverLifetime - SessionExpirySafetyMargin
                : TimeSpan.Zero;
            if (refreshAge > SessionRefreshCeiling)
                refreshAge = SessionRefreshCeiling;

            return refreshAge > TimeSpan.Zero && DateTimeOffset.UtcNow - session.CreatedAtUtc < refreshAge;
        }

        private static void InvalidateSession(bool advanced)
        {
            if (advanced) _advancedSession = null;
            else _basicSession = null;
        }

        private static string Limit(string value, int maximum) =>
            value.Length <= maximum ? value : value[..maximum];

        private sealed record StoreCollectionsTicketResponse(string ServiceTicket, string PublisherUserId);
        private sealed record StoreSessionRequest(int SchemaVersion, string CollectionsId);
        private sealed record GatewaySessionResponse(string AccessToken, string? Tier, int ExpiresInSeconds);
        private sealed record GatewaySession(string AccessToken, string Tier, DateTimeOffset CreatedAtUtc, int ExpiresInSeconds);

        private sealed record GatewaySessionResult(bool Succeeded, GatewaySession? Session, bool RequiresSubscription, string Reason)
        {
            public static GatewaySessionResult Success(GatewaySession session) => new(true, session, false, string.Empty);
            public static GatewaySessionResult Unavailable(string reason) => new(false, null, false, reason);
            public static GatewaySessionResult SubscriptionRequired(string reason) => new(false, null, true, reason);
        }

        private sealed record CloudAiRequest(
            int SchemaVersion,
            string RequestId,
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
