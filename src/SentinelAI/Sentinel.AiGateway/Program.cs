using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("openai", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddHttpClient("entra", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("store", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<GatewaySecurity>();

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

app.MapGet("/health", () => Results.Ok(new
{
    service = "Sentinel AI Gateway",
    status = "healthy",
    providerConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")),
    storeConfigured = StoreConfigurationPresent(),
    sessionSigningConfigured = SessionSigningConfigured()
}));

// This endpoint returns a narrowly scoped Microsoft Entra collections-creation token.
// It never returns the Store service token or the Entra client secret. The Windows app
// passes this short-lived ticket to StoreContext.GetCustomerCollectionsIdAsync.
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

// Free Ask Sentinel remains available, but only through a server-issued Basic session.
// The session establishes an authenticated gateway capability; it is not a paid entitlement.
app.MapPost("/v1/session/free", (HttpContext context, GatewaySecurity security) =>
{
    string subject = "free:" + HashForLog(context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    string? token = security.IssueSession("Basic", subject);
    return string.IsNullOrWhiteSpace(token)
        ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new GatewaySessionResponse(token, "Basic", 600));
});

// Paid sessions are issued only after the gateway verifies the Microsoft Store entitlement.
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

    // Do not write user prompt/response contents to application logs. Retention and any
    // future report storage must be implemented as an explicit privacy-controlled subsystem.
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
    int requestedBudget = Math.Clamp(request.MaximumTotalTokens, 1, tierMaximum);
    int maxOutputTokens = Math.Clamp(requestedBudget / 3, 192, advanced ? 700 : 400);

    string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    string economyModel = Environment.GetEnvironmentVariable("SENTINEL_AI_ECONOMY_MODEL") ?? "gpt-5.6-luna";
    string advancedModel = Environment.GetEnvironmentVariable("SENTINEL_AI_ADVANCED_MODEL") ?? "gpt-5.6-terra";
    string model = advanced ? advancedModel : economyModel;
    string reasoningEffort = advanced ? "low" : "none";

    var prompt = new
    {
        model,
        max_output_tokens = maxOutputTokens,
        reasoning = new { effort = reasoningEffort },
        input = new object[]
        {
            new
            {
                role = "system",
                content = new object[]
                {
                    new
                    {
                        type = "input_text",
                        text = "You are the advisory reasoning layer for Sentinel AI, a Windows monitoring and repair application. Use only the supplied verified evidence. Clearly separate facts from inference. Never claim a repair succeeded, never authorize a system change, and never invent missing evidence. If evidence is insufficient, say exactly what additional local evidence is needed. Keep the answer concise for a nontechnical user."
                    }
                }
            },
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "input_text", text = request.Evidence }
                }
            }
        }
    };

    using IDisposable? providerLease = await security.TryEnterProviderAsync(cancellationToken).ConfigureAwait(false);
    if (providerLease is null)
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);

    using HttpClient client = httpClientFactory.CreateClient("openai");
    using HttpRequestMessage message = new(HttpMethod.Post, "responses")
    {
        Content = JsonContent.Create(prompt)
    };
    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    using HttpResponseMessage response = await client.SendAsync(
        message,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken).ConfigureAwait(false);

    await using Stream providerStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    using JsonDocument document = await JsonDocument.ParseAsync(providerStream,
        new JsonDocumentOptions { MaxDepth = 64 }, cancellationToken).ConfigureAwait(false);

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"OPENAI_GATEWAY_FAILURE status={(int)response.StatusCode}");
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    JsonElement root = document.RootElement;
    string answer = ExtractOutputText(root);
    if (string.IsNullOrWhiteSpace(answer))
    {
        Console.Error.WriteLine("OPENAI_GATEWAY_EMPTY_RESPONSE");
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    int inputTokens = ReadUsage(root, "input_tokens");
    int outputTokens = ReadUsage(root, "output_tokens");
    bool moreEvidence = IndicatesMoreEvidence(answer);
    int confidence = moreEvidence ? 55 : 75;

    return Results.Ok(new SentinelAiResponse(
        Answer: Limit(answer.Trim(), 8_000),
        Provider: "OpenAI",
        Model: model,
        InputTokens: inputTokens,
        OutputTokens: outputTokens,
        ConfidencePercent: confidence,
        RequiresMoreEvidence: moreEvidence));
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

static string ExtractOutputText(JsonElement root)
{
    if (root.TryGetProperty("output_text", out JsonElement direct) && direct.ValueKind == JsonValueKind.String)
    {
        string? text = direct.GetString();
        if (!string.IsNullOrWhiteSpace(text)) return text;
    }

    if (!root.TryGetProperty("output", out JsonElement output) || output.ValueKind != JsonValueKind.Array)
        return string.Empty;

    foreach (JsonElement item in output.EnumerateArray())
    {
        if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
            continue;

        foreach (JsonElement part in content.EnumerateArray())
        {
            if (part.TryGetProperty("type", out JsonElement type) &&
                type.GetString()?.Equals("output_text", StringComparison.OrdinalIgnoreCase) == true &&
                part.TryGetProperty("text", out JsonElement text))
            {
                string? value = text.GetString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
    }

    return string.Empty;
}

static int ReadUsage(JsonElement root, string property)
{
    if (root.TryGetProperty("usage", out JsonElement usage) &&
        usage.TryGetProperty(property, out JsonElement value) &&
        value.TryGetInt32(out int number))
        return Math.Max(0, number);
    return 0;
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
    bool RequiresMoreEvidence);

public sealed record AiContentReportRequest(
    int SchemaVersion,
    string? ResponseId,
    string? Category,
    string? Comments,
    string? ResponseText,
    DateTimeOffset ReportedAtUtc);
