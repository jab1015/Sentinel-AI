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
    /// No provider API secret is stored in Sentinel. The endpoint is intentionally
    /// configuration-driven so release builds can be pointed at the production gateway.
    /// </summary>
    public sealed class CloudAiGatewayClient
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        private readonly HttpClient _httpClient;
        private readonly Uri? _endpoint;

        public CloudAiGatewayClient()
        {
            _httpClient = new HttpClient { Timeout = RequestTimeout };
            string? endpoint = Environment.GetEnvironmentVariable("SENTINEL_AI_GATEWAY_URL");
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
                    Answer: body.Answer.Trim(),
                    Provider: body.Provider?.Trim() ?? string.Empty,
                    Model: body.Model?.Trim() ?? string.Empty,
                    InputTokens: Math.Max(0, body.InputTokens),
                    OutputTokens: Math.Max(0, body.OutputTokens),
                    ConfidencePercent: Math.Clamp(body.ConfidencePercent, 0, 100),
                    RequiresMoreEvidence: body.RequiresMoreEvidence,
                    Reason: "Secure AI gateway returned an advisory analysis. Sentinel must still validate any actionable conclusion against local evidence.");
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
        string Reason)
    {
        public static CloudAiResult NotUsed(string reason) =>
            new(false, true, string.Empty, string.Empty, string.Empty, 0, 0, 0, false, reason);

        public static CloudAiResult Unavailable(string reason) =>
            new(false, false, string.Empty, string.Empty, string.Empty, 0, 0, 0, false, reason);
    }
}
