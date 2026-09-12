using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed class GatewaySecurity
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReplayLifetime = TimeSpan.FromMinutes(15);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seenRequests = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _providerConcurrency;
    private readonly object _replayGate = new();
    private readonly int _maximumReplayEntries;

    internal GatewaySecurity(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _providerConcurrency = new SemaphoreSlim(ReadInt("SENTINEL_AI_MAX_CONCURRENT_PROVIDER_REQUESTS", 8, 1, 64));
        _maximumReplayEntries = ReadInt("SENTINEL_AI_MAX_REPLAY_ENTRIES", 50_000, 100, 250_000);
    }

    internal async Task<string?> GetCollectionsCreationTicketAsync(CancellationToken cancellationToken)
    {
        return await AcquireEntraTokenAsync(
            "https://onestore.microsoft.com/b2b/keys/create/collections/.default",
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<StoreEntitlementResult> VerifyPaidEntitlementAsync(
        string collectionsId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(collectionsId) || collectionsId.Length > 16_384)
            return StoreEntitlementResult.Invalid("The Microsoft Store collections identifier is missing or invalid.");

        string[] productIds = ReadCsv("SENTINEL_STORE_PAID_PRODUCT_IDS",
            new[] { "9N67THV2Z1GP" });
        if (productIds.Length == 0)
            return StoreEntitlementResult.Unavailable("No paid Store product IDs are configured on the gateway.");

        string? serviceToken = await AcquireEntraTokenAsync(
            "https://onestore.microsoft.com/.default",
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(serviceToken))
            return StoreEntitlementResult.Unavailable("The gateway could not obtain a Microsoft Store service token.");

        object requestBody = new
        {
            maxPageSize = 100,
            excludeDuplicates = true,
            validityType = "Valid",
            productSkuIds = productIds.Select(id => new { productId = id }).ToArray(),
            beneficiaries = new[]
            {
                new
                {
                    identityType = "b2b",
                    identityValue = collectionsId,
                    localTicketReference = string.Empty
                }
            },
            sbx = Environment.GetEnvironmentVariable("SENTINEL_STORE_SANDBOX")?.Trim() is { Length: > 0 } sandbox
                ? sandbox
                : "RETAIL"
        };

        using HttpClient client = _httpClientFactory.CreateClient("store");
        using HttpRequestMessage message = new(HttpMethod.Post,
            "https://collections.mp.microsoft.com/v9.0/collections/publisherQuery")
        {
            Content = JsonContent.Create(requestBody)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        message.Headers.UserAgent.ParseAdd("Sentinel-AI-Gateway/1.0");

        using HttpResponseMessage response = await client.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"STORE_ENTITLEMENT_QUERY_FAILED status={(int)response.StatusCode}");
            return StoreEntitlementResult.Unavailable("Microsoft Store entitlement verification is temporarily unavailable.");
        }

        using JsonDocument? document = await BoundedHttpJson.TryReadAsync(
            response.Content,
            BoundedHttpJson.MaximumStoreResponseBytes,
            maximumDepth: 32,
            BoundedHttpJson.StoreBodyTimeout,
            cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            Console.Error.WriteLine("STORE_ENTITLEMENT_QUERY_INVALID_OR_OVERSIZED_RESPONSE");
            return StoreEntitlementResult.Unavailable("Microsoft Store entitlement verification returned invalid data.");
        }

        if (!document.RootElement.TryGetProperty("items", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array)
            return StoreEntitlementResult.Invalid("Microsoft Store returned no active Sentinel entitlement.");

        HashSet<string> expected = new(productIds, StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in items.EnumerateArray())
        {
            string productId = ReadString(item, "productId");
            string status = ReadString(item, "status");
            if (expected.Contains(productId) && status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                return StoreEntitlementResult.Active(productId);
        }

        return StoreEntitlementResult.Invalid("No active paid Sentinel entitlement was verified by Microsoft Store.");
    }

    internal string? IssueSession(string tier, string subject)
    {
        byte[]? key = ReadSigningKey();
        if (key is null) return null;

        string normalizedTier = tier.Equals("Advanced", StringComparison.OrdinalIgnoreCase) ? "Advanced" : "Basic";
        SessionPayload payload = new(
            Version: 1,
            Subject: Limit(subject, 128),
            Tier: normalizedTier,
            ExpiresUnixSeconds: DateTimeOffset.UtcNow.Add(SessionLifetime).ToUnixTimeSeconds(),
            TokenId: Guid.NewGuid().ToString("N"));

        string payloadJson = JsonSerializer.Serialize(payload);
        string encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        byte[] signature = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(encodedPayload));
        return encodedPayload + "." + Base64UrlEncode(signature);
    }

    internal SessionValidationResult ValidateSession(string? authorizationHeader, string requestId, string requestedTier)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return SessionValidationResult.Denied("A gateway session is required.");

        string token = authorizationHeader[7..].Trim();
        string[] parts = token.Split('.');
        if (parts.Length != 2)
            return SessionValidationResult.Denied("The gateway session is invalid.");

        byte[]? key = ReadSigningKey();
        if (key is null)
            return SessionValidationResult.Unavailable("Gateway session signing is not configured.");

        byte[] providedSignature;
        byte[] payloadBytes;
        try
        {
            providedSignature = Base64UrlDecode(parts[1]);
            payloadBytes = Base64UrlDecode(parts[0]);
        }
        catch (FormatException)
        {
            return SessionValidationResult.Denied("The gateway session is malformed.");
        }

        byte[] expectedSignature = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(parts[0]));
        if (providedSignature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
            return SessionValidationResult.Denied("The gateway session signature is invalid.");

        SessionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SessionPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            return SessionValidationResult.Denied("The gateway session payload is invalid.");
        }

        if (payload is null || payload.Version != 1 || string.IsNullOrWhiteSpace(payload.Subject) ||
            string.IsNullOrWhiteSpace(payload.TokenId) ||
            payload.ExpiresUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return SessionValidationResult.Denied("The gateway session is expired or invalid.");

        bool advancedAuthorized = payload.Tier.Equals("Advanced", StringComparison.OrdinalIgnoreCase);
        bool advancedRequested = requestedTier.Equals("Advanced", StringComparison.OrdinalIgnoreCase);
        if (advancedRequested && !advancedAuthorized)
            return SessionValidationResult.Denied("This session is not entitled to Advanced AI.");

        if (!Guid.TryParse(requestId, out _))
            return SessionValidationResult.Denied("A valid unique request ID is required.");

        string replayKey = payload.TokenId + ":" + requestId;
        lock (_replayGate)
        {
            PruneReplayCacheLocked();
            if (_seenRequests.ContainsKey(replayKey))
                return SessionValidationResult.Denied("This request has already been processed.");

            if (_seenRequests.Count >= _maximumReplayEntries)
                return SessionValidationResult.Unavailable("Gateway replay protection is at capacity and failed closed.");

            if (!_seenRequests.TryAdd(replayKey, DateTimeOffset.UtcNow.Add(ReplayLifetime)))
                return SessionValidationResult.Denied("This request has already been processed.");
        }

        return SessionValidationResult.Allowed(payload.Subject, advancedRequested && advancedAuthorized ? "Advanced" : "Basic");
    }

    internal async Task<IDisposable?> TryEnterProviderAsync(CancellationToken cancellationToken)
    {
        if (!await _providerConcurrency.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            return null;
        return new SemaphoreLease(_providerConcurrency);
    }

    private async Task<string?> AcquireEntraTokenAsync(string scope, CancellationToken cancellationToken)
    {
        string? tenantId = Environment.GetEnvironmentVariable("SENTINEL_STORE_TENANT_ID")?.Trim();
        string? clientId = Environment.GetEnvironmentVariable("SENTINEL_STORE_CLIENT_ID")?.Trim();
        string? clientSecret = Environment.GetEnvironmentVariable("SENTINEL_STORE_CLIENT_SECRET")?.Trim();
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return null;

        using HttpClient client = _httpClientFactory.CreateClient("entra");
        using FormUrlEncodedContent content = new(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = scope,
            ["grant_type"] = "client_credentials"
        });
        using HttpRequestMessage message = new(
            HttpMethod.Post,
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenantId)}/oauth2/v2.0/token")
        {
            Content = content
        };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            Console.Error.WriteLine("ENTRA_TOKEN_NETWORK_FAILURE");
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"ENTRA_TOKEN_FAILED status={(int)response.StatusCode}");
                return null;
            }

            using JsonDocument? document = await BoundedHttpJson.TryReadAsync(
                response.Content,
                BoundedHttpJson.MaximumEntraResponseBytes,
                maximumDepth: 16,
                BoundedHttpJson.EntraBodyTimeout,
                cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                Console.Error.WriteLine("ENTRA_TOKEN_INVALID_OR_OVERSIZED_RESPONSE");
                return null;
            }

            return document.RootElement.TryGetProperty("access_token", out JsonElement accessToken)
                ? accessToken.GetString()
                : null;
        }
    }

    private void PruneReplayCacheLocked()
    {
        if (_seenRequests.Count < Math.Min(2_000, _maximumReplayEntries)) return;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach ((string key, DateTimeOffset expires) in _seenRequests)
            if (expires <= now) _seenRequests.TryRemove(key, out _);
    }

    private static byte[]? ReadSigningKey()
    {
        string? raw = Environment.GetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY")?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        byte[] key = Encoding.UTF8.GetBytes(raw);
        return key.Length >= 32 ? key : null;
    }

    private static string[] ReadCsv(string name, string[] fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        string[] values = string.IsNullOrWhiteSpace(raw)
            ? fallback
            : raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return values.Where(v => v.Length is > 0 and <= 64).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int ReadInt(string name, int fallback, int min, int max) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? Math.Clamp(value, min, max) : fallback;

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", 0 => string.Empty, _ => throw new FormatException() };
        return Convert.FromBase64String(padded);
    }

    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    private sealed record SessionPayload(int Version, string Subject, string Tier, long ExpiresUnixSeconds, string TokenId);

    private sealed class SemaphoreLease : IDisposable
    {
        private SemaphoreSlim? _semaphore;
        internal SemaphoreLease(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}

internal sealed record StoreEntitlementResult(bool Available, bool IsActive, string ProductId, string Reason)
{
    internal static StoreEntitlementResult Active(string productId) => new(true, true, productId, "Active paid entitlement verified by Microsoft Store.");
    internal static StoreEntitlementResult Invalid(string reason) => new(true, false, string.Empty, reason);
    internal static StoreEntitlementResult Unavailable(string reason) => new(false, false, string.Empty, reason);
}

internal sealed record SessionValidationResult(bool Available, bool Authorized, string Subject, string Tier, string Reason)
{
    internal static SessionValidationResult Allowed(string subject, string tier) => new(true, true, subject, tier, string.Empty);
    internal static SessionValidationResult Denied(string reason) => new(true, false, string.Empty, string.Empty, reason);
    internal static SessionValidationResult Unavailable(string reason) => new(false, false, string.Empty, string.Empty, reason);
}
