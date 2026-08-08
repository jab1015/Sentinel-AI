using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("openai", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(45);
});

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
    providerConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
}));

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
        eventType = "AI_CONTENT_REPORT",
        schemaVersion = 1,
        responseId,
        category,
        comments,
        responseText,
        reportedAtUtc = request.ReportedAtUtc == default ? DateTimeOffset.UtcNow : request.ReportedAtUtc,
        receivedAtUtc = DateTimeOffset.UtcNow
    };

    Console.WriteLine("AI_CONTENT_REPORT " + JsonSerializer.Serialize(auditRecord));
    return Results.Ok(new { accepted = true, responseId });
});

app.MapPost("/v1/analyze", async (
    SentinelAiRequest request,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    if (request.SchemaVersion != 1)
        return Results.BadRequest(new { error = "Unsupported schema version." });

    if (string.IsNullOrWhiteSpace(request.Evidence) || request.Evidence.Length > 8_000)
        return Results.BadRequest(new { error = "Evidence payload is empty or exceeds the gateway limit." });

    int requestedBudget = Math.Clamp(request.MaximumTotalTokens, 1, 2_500);
    int maxOutputTokens = Math.Clamp(requestedBudget / 3, 192, 700);

    string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    bool advanced = request.ModelTier.Equals("Advanced", StringComparison.OrdinalIgnoreCase);
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

    using HttpClient client = httpClientFactory.CreateClient("openai");
    using HttpRequestMessage message = new(HttpMethod.Post, "responses")
    {
        Content = JsonContent.Create(prompt)
    };
    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    using HttpResponseMessage response = await client.SendAsync(message, cancellationToken);
    string raw = await response.Content.ReadAsStringAsync(cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"OpenAI gateway failure {(int)response.StatusCode}: {SafeProviderError(raw)}");
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    using JsonDocument document = JsonDocument.Parse(raw);
    JsonElement root = document.RootElement;
    string answer = ExtractOutputText(root);
    if (string.IsNullOrWhiteSpace(answer))
    {
        Console.Error.WriteLine($"OpenAI returned HTTP 200 but no output text: {SafeProviderError(raw)}");
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    int inputTokens = ReadUsage(root, "input_tokens");
    int outputTokens = ReadUsage(root, "output_tokens");
    bool moreEvidence = IndicatesMoreEvidence(answer);
    int confidence = moreEvidence ? 55 : 75;

    return Results.Ok(new SentinelAiResponse(
        Answer: answer.Trim(),
        Provider: "OpenAI",
        Model: model,
        InputTokens: inputTokens,
        OutputTokens: outputTokens,
        ConfidencePercent: confidence,
        RequiresMoreEvidence: moreEvidence));
});

app.Run();

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

static string SafeProviderError(string raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return "empty provider response";
    string oneLine = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
    return oneLine.Length <= 1000 ? oneLine : oneLine[..1000];
}

static string Limit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

public sealed record SentinelAiRequest(
    int SchemaVersion,
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
