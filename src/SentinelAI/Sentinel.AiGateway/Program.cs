using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 64 * 1024;
});

builder.Services.AddHttpClient("openai", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddHttpClient("entra", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("store", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<GatewaySecurity>();
builder.Services.AddSingleton<PrivacyCapabilitySecurity>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        string key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = ReadInt("SENTINEL_AI_REQUESTS_PER_MINUTE", 20, 1, 120),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var app = builder.Build();
app.UseRateLimiter();
app.MapPrivacyCapabilityEndpoints();

app.MapGet("/health", () => Results.Ok(new
{
    service = "Sentinel AI Gateway",
    status = "healthy",
    providerConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")),
    storeConfigured = StoreConfigurationPresent(),
    sessionSigningConfigured = SessionSigningConfigured(),
    privacyCapabilityReplayState = "process-local"
}));

app.MapGet("/v1/store/collections-ticket", async (
    GatewaySecurity security,
    CancellationToken cancellationToken) =>
{
    string? ticket = await security.GetCollectionsCreationTicketAsync(cancellationToken).ConfigureAwait(false);
    if (string.IsNullOrWhiteSpace(ticket))
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    return Results.Ok(new StoreCollectionsTicketResponse(
        ServiceTicket: ticket,
        PublisherUserId: Guid.NewGuid().ToString("N")));
});

app.MapPost("/v1/session/free", (HttpContext context, GatewaySecurity security) =>
{
    string subject = "free:" + HashForLog(context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    string? token = security.IssueSession("Basic", subject);
    return string.IsNullOrWhiteSpace(token)
        ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new GatewaySessionResponse(token, "Basic", 600));
});

app.MapPost("/v1/session/store", async (
    StoreSessionRequest request,
    GatewaySecurity security,
    CancellationToken cancellationToken) =>
{
    if (request.SchemaVersion != 1 || string.IsNullOrWhiteSpace(request.CollectionsId))
        return Results.BadRequest(new { error = "A Microsoft Store collections identifier is required." });

    StoreEntitlementResult entitlement = await security.VerifyPaidEntitlementAsync(
        request.CollectionsId,
        cancellationToken).ConfigureAwait(false);

    if (!entitlement.Available)
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    if (!entitlement.IsActive)
        return Results.Json(new { error = "No active paid Sentinel entitlement was verified." }, statusCode: StatusCodes.Status403Forbidden);

    string subject = "store:" + HashForLog(request.CollectionsId);
    string? token = security.IssueSession("Advanced", subject);
    return string.IsNullOrWhiteSpace(token)
        ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new GatewaySessionResponse(token, "Advanced", 600));
});

app.MapPost("/v1/report-ai-content", (AiContentReportRequest request) =>
{
    if (request.SchemaVersion != 1)
        return Results.BadRequest(new { error = "Unsupported schema version." });

    string responseId = Limit((request.ResponseId ?? string.Empty).Trim(), 64);
    string category = Limit((request.Category ?? string.Empty).Trim(), 64);
    string comments = Limit((request.Comments ?? string.Empty).Trim(), 1000);
    string responseText = Limit((request.ResponseText ?? string.Empty).Trim(), 2500);

    if (string.IsNullOrWhiteSpace(responseId) || string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(responseText))
        return Results.BadRequest(new { error = "Report is incomplete." });

    var auditRecord = new
    {
        eventType = "AI_CONTENT_REPORT_RECEIVED",
        schemaVersion = 1,
        responseId,
        category,
        commentsLength = comments.Length,
        responseTextLength = responseText.Length,
        reportedAtUtc = request.ReportedAtUtc == default ? DateTimeOffset.UtcNow : request.ReportedAtUtc,
        receivedAtUtc = DateTimeOffset.UtcNow
    };

    Console.WriteLine("AI_CONTENT_REPORT " + JsonSerializer.Serialize(auditRecord));
    return Results.Ok(new { accepted = true, responseId });
});

app.MapPost("/v1/analyze", async (
    HttpContext context,
    SentinelAiRequest request,
    GatewaySecurity security,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    if (request.SchemaVersion != 1)
        return Results.BadRequest(new { error = "Unsupported schema version." });

    if (string.IsNullOrWhiteSpace(request.RequestId) ||
        string.IsNullOrWhiteSpace(request.Evidence) || request.Evidence.Length > 8_000)
        return Results.BadRequest(new { error = "Request ID/evidence is missing or evidence exceeds the gateway limit." });

    SessionValidationResult authorization = security.ValidateSession(
        context.Request.Headers.Authorization.ToString(),
        request.RequestId,
        request.ModelTier);

    if (!authorization.Available)
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    if (!authorization.Authorized)
    {
        Console.Error.WriteLine($"AI_AUTHORIZATION_DENIED reason={Limit(authorization.Reason, 120)}");
        return Results.Json(new { error = authorization.Reason }, statusCode: StatusCodes.Status401Unauthorized);
    }

    bool advanced = authorization.Tier.Equals("Advanced", StringComparison.OrdinalIgnoreCase);
    int tierMaximum = advanced
        ? ReadInt("SENTINEL_AI_ADVANCED_MAX_TOTAL_TOKENS", 2_500, 256, 4_000)
        : ReadInt("SENTINEL_AI_BASIC_MAX_TOTAL_TOKENS", 900, 192, 1_500);

    const int MinimumTotalTokenBudget = 192;
    if (request.MaximumTotalTokens < MinimumTotalTokenBudget || request.MaximumTotalTokens > tierMaximum)
    {
        return Results.BadRequest(new
        {
            error = $"The requested total token budget must be between {MinimumTotalTokenBudget} and {tierMaximum} for this tier."
        });
    }

    int requestedBudget = request.MaximumTotalTokens;
    int maxOutputTokens = Math.Min(advanced ? 700 : 400, Math.Max(64, requestedBudget / 3));

    string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    string economyModel = Environment.GetEnvironmentVariable("SENTINEL_AI_ECONOMY_MODEL") ?? "gpt-5.6-luna";
    string advancedModel = Environment.GetEnvironmentVariable("SENTINEL_AI_ADVANCED_MODEL") ?? "gpt-5.6-terra";
    string model = advanced ? advancedModel : economyModel;
    string reasoningEffort = advanced ? "low" : "none";

    Dictionary<string, object?> prompt = AiProviderRequestFactory.Create(
        model,
        maxOutputTokens,
        reasoningEffort,
        request.Evidence,
        advanced,
        request.Purpose ?? string.Empty);

    using IDisposable? providerLease = await security.TryEnterProviderAsync(cancellationToken).ConfigureAwait(false);
    if (providerLease is null)
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);

    using HttpClient client = httpClientFactory.CreateClient("openai");
    using HttpRequestMessage message = new(HttpMethod.Post, "responses")
    {
        Content = JsonContent.Create(prompt)
    };
    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    HttpResponseMessage response;
    try
    {
        response = await client.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        Console.Error.WriteLine("OPENAI_GATEWAY_TIMEOUT");
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }
    catch (HttpRequestException)
    {
        Console.Error.WriteLine("OPENAI_GATEWAY_NETWORK_FAILURE");
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    using (response)
    {
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"OPENAI_GATEWAY_FAILURE status={(int)response.StatusCode}");
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        using JsonDocument? document = await BoundedHttpJson.TryReadAsync(
            response.Content,
            BoundedHttpJson.MaximumProviderResponseBytes,
            maximumDepth: 64,
            BoundedHttpJson.ProviderBodyTimeout,
            cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            Console.Error.WriteLine("OPENAI_GATEWAY_INVALID_OVERSIZED_OR_STALLED_RESPONSE");
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        AiProviderParsedResponse parsed = AiProviderResponseParser.Parse(document.RootElement);
        if (string.IsNullOrWhiteSpace(parsed.Answer))
        {
            Console.Error.WriteLine("OPENAI_GATEWAY_EMPTY_RESPONSE");
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        long totalTokens = (long)parsed.InputTokens + parsed.OutputTokens;
        if (parsed.InputTokens <= 0 || parsed.OutputTokens < 0 || totalTokens > requestedBudget)
        {
            Console.Error.WriteLine($"OPENAI_GATEWAY_TOKEN_BUDGET_VIOLATION input={parsed.InputTokens} output={parsed.OutputTokens} budget={requestedBudget}");
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        // Defense in depth: a provider must not be able to smuggle web-derived material into
        // a Basic or non-research request even if its response shape unexpectedly contains it.
        bool webAuthorized = AiProviderRequestFactory.ShouldEnableWebSearch(advanced, request.Purpose);
        bool usedWebSearch = webAuthorized && parsed.UsedWebSearch;
        IReadOnlyList<AiProviderCitation> citations = usedWebSearch
            ? parsed.Citations
            : Array.Empty<AiProviderCitation>();
        IReadOnlyList<AiProviderSource> sources = usedWebSearch
            ? parsed.Sources
            : Array.Empty<AiProviderSource>();

        bool moreEvidence = IndicatesMoreEvidence(parsed.Answer);
        int confidence = moreEvidence ? 55 : 75;

        return Results.Ok(new SentinelAiResponse(
            Answer: Limit(parsed.Answer.Trim(), 8_000),
            Provider: "OpenAI",
            Model: model,
            InputTokens: parsed.InputTokens,
            OutputTokens: parsed.OutputTokens,
            ConfidencePercent: confidence,
            RequiresMoreEvidence: moreEvidence,
            UsedWebSearch: usedWebSearch,
            Citations: citations,
            Sources: sources));
    }
});

app.Run();

static bool StoreConfigurationPresent() =>
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SENTINEL_STORE_TENANT_ID")) &&
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SENTINEL_STORE_CLIENT_ID")) &&
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SENTINEL_STORE_CLIENT_SECRET"));

static bool SessionSigningConfigured() =>
    Encoding.UTF8.GetByteCount(Environment.GetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY")?.Trim() ?? string.Empty) >= 32;

static string HashForLog(string value)
{
    byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(digest.AsSpan(0, 12));
}

static int ReadInt(string name, int fallback, int min, int max)
{
    return int.TryParse(Environment.GetEnvironmentVariable(name), out int parsed)
        ? Math.Clamp(parsed, min, max)
        : fallback;
}

static bool IndicatesMoreEvidence(string answer)
{
    string value = answer.ToLowerInvariant();
    return value.Contains("insufficient evidence") ||
           value.Contains("need more evidence") ||
           value.Contains("additional evidence") ||
           value.Contains("cannot determine") ||
           value.Contains("can't determine");
}

static string Limit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

public sealed record StoreCollectionsTicketResponse(string ServiceTicket, string PublisherUserId);
public sealed record StoreSessionRequest(int SchemaVersion, string CollectionsId);
public sealed record GatewaySessionResponse(string AccessToken, string Tier, int ExpiresInSeconds);

public sealed record SentinelAiRequest(
    int SchemaVersion,
    string RequestId,
    string Purpose,
    string ModelTier,
    int MaximumTotalTokens,
    string Evidence);

public sealed record SentinelAiResponse(
    string Answer,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    int ConfidencePercent,
    bool RequiresMoreEvidence,
    bool UsedWebSearch,
    IReadOnlyList<AiProviderCitation> Citations,
    IReadOnlyList<AiProviderSource> Sources);

public sealed record AiContentReportRequest(
    int SchemaVersion,
    string? ResponseId,
    string? Category,
    string? Comments,
    string? ResponseText,
    DateTimeOffset ReportedAtUtc);
