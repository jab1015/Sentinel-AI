using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

Environment.SetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY", "acceptance-harness-signing-key-32-bytes-minimum");

var security = new GatewaySecurity(new FakeHttpClientFactory());
int failures = 0;

void Check(bool condition, string name)
{
    Console.WriteLine($"{name}: {(condition ? "PASS" : "FAIL")}");
    if (!condition) failures++;
}

string? basicToken = security.IssueSession("Basic", "acceptance-basic");
Check(!string.IsNullOrWhiteSpace(basicToken), "Basic session issued");
Check(security.IssueSession("Unknown", "acceptance-unknown") is null, "Unknown session tier is rejected");
Check(security.IssueSession("Basic", "   ") is null, "Blank session subject is rejected");

string basicRequest = Guid.NewGuid().ToString();
SessionValidationResult basic = security.ValidateSession("Bearer " + basicToken, basicRequest, "Basic");
Check(basic.Available && basic.Authorized && basic.Tier == "Basic", "Basic session authorizes Basic request");

SessionValidationResult basicReplay = security.ValidateSession("Bearer " + basicToken, basicRequest, "Basic");
Check(!basicReplay.Authorized && basicReplay.Reason.Contains("already", StringComparison.OrdinalIgnoreCase), "Replay is rejected");

Guid alternateEncodingId = Guid.NewGuid();
SessionValidationResult alternateFirst = security.ValidateSession(
    "Bearer " + basicToken,
    alternateEncodingId.ToString("D"),
    "Basic");
SessionValidationResult alternateReplay = security.ValidateSession(
    "Bearer " + basicToken,
    alternateEncodingId.ToString("N"),
    "Basic");
Check(alternateFirst.Authorized && !alternateReplay.Authorized && alternateReplay.Reason.Contains("already", StringComparison.OrdinalIgnoreCase),
    "Equivalent GUID encodings share one replay identity");

SessionValidationResult unknownTier = security.ValidateSession("Bearer " + basicToken, Guid.NewGuid().ToString(), "Unknown");
Check(!unknownTier.Authorized && unknownTier.Reason.Contains("unsupported", StringComparison.OrdinalIgnoreCase),
    "Unknown requested AI tier is rejected");

SessionValidationResult upgrade = security.ValidateSession("Bearer " + basicToken, Guid.NewGuid().ToString(), "Advanced");
Check(!upgrade.Authorized, "Basic session cannot authorize Advanced request");

string? advancedToken = security.IssueSession("Advanced", "acceptance-paid");
Check(!string.IsNullOrWhiteSpace(advancedToken), "Advanced session issued");
SessionValidationResult advanced = security.ValidateSession("Bearer " + advancedToken, Guid.NewGuid().ToString(), "Advanced");
Check(advanced.Authorized && advanced.Tier == "Advanced", "Advanced session authorizes Advanced request");

string tampered = (advancedToken ?? string.Empty) + "x";
SessionValidationResult tamperedResult = security.ValidateSession("Bearer " + tampered, Guid.NewGuid().ToString(), "Advanced");
Check(!tamperedResult.Authorized, "Tampered session is rejected");

SessionValidationResult anonymous = security.ValidateSession(null, Guid.NewGuid().ToString(), "Basic");
Check(!anonymous.Authorized, "Anonymous analyze request is rejected");

SessionValidationResult badRequestId = security.ValidateSession("Bearer " + basicToken, "not-a-guid", "Basic");
Check(!badRequestId.Authorized, "Malformed request ID is rejected");

Dictionary<string, object?> basicProviderRequest = AiProviderRequestFactory.Create(
    "gpt-basic", 300, "none", "SENTINEL_AI_EVIDENCE_V1", advanced: false, purpose: "ask-sentinel-basic");
using (JsonDocument basicProviderJson = JsonDocument.Parse(JsonSerializer.Serialize(basicProviderRequest)))
{
    JsonElement root = basicProviderJson.RootElement;
    Check(root.TryGetProperty("store", out JsonElement store) && store.ValueKind == JsonValueKind.False,
        "Provider response storage is explicitly disabled for Basic AI");
    Check(!root.TryGetProperty("tools", out _),
        "Basic AI request cannot enable provider web search");
    Check(!root.TryGetProperty("include", out _),
        "Basic AI request does not request web-search source metadata");
}

Dictionary<string, object?> advancedNonResearchRequest = AiProviderRequestFactory.Create(
    "gpt-advanced", 600, "low", "SENTINEL_AI_EVIDENCE_V1", advanced: true, purpose: "another-purpose");
using (JsonDocument advancedNonResearchJson = JsonDocument.Parse(JsonSerializer.Serialize(advancedNonResearchRequest)))
{
    JsonElement root = advancedNonResearchJson.RootElement;
    Check(root.TryGetProperty("store", out JsonElement store) && store.ValueKind == JsonValueKind.False,
        "Provider response storage is explicitly disabled for Advanced AI");
    Check(!root.TryGetProperty("tools", out _),
        "Advanced non-research request cannot enable provider web search");
}

Dictionary<string, object?> advancedResearchRequest = AiProviderRequestFactory.Create(
    "gpt-advanced", 600, "low", "SENTINEL_AI_EVIDENCE_V1", advanced: true, purpose: "external-investigation");
using (JsonDocument advancedResearchJson = JsonDocument.Parse(JsonSerializer.Serialize(advancedResearchRequest)))
{
    JsonElement root = advancedResearchJson.RootElement;
    Check(root.TryGetProperty("store", out JsonElement store) && store.ValueKind == JsonValueKind.False,
        "External research also disables provider response storage");
    Check(root.TryGetProperty("tools", out JsonElement tools) &&
          tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() == 1 &&
          tools[0].TryGetProperty("type", out JsonElement toolType) &&
          toolType.GetString() == "web_search",
        "Only Advanced external investigation enables the web_search tool");
    Check(root.TryGetProperty("max_tool_calls", out JsonElement maxToolCalls) &&
          maxToolCalls.TryGetInt32(out int toolCallLimit) &&
          toolCallLimit == AiProviderRequestFactory.MaximumWebSearchToolCalls && toolCallLimit <= 2,
        "External web research is bounded to at most two tool calls");
    Check(root.TryGetProperty("include", out JsonElement include) &&
          include.ValueKind == JsonValueKind.Array &&
          include.EnumerateArray().Any(item =>
              item.ValueKind == JsonValueKind.String &&
              item.GetString() == "web_search_call.action.sources"),
        "External web research requests attributable source metadata");
}

const string providerResponse = """
{
  "output": [
    {
      "type": "web_search_call",
      "action": {
        "type": "search",
        "sources": [
          { "type": "url", "url": "https://www.nvidia.com/download/index.aspx", "title": "NVIDIA Driver Downloads" }
        ]
      }
    },
    {
      "type": "message",
      "content": [
        {
          "type": "output_text",
          "text": "NVIDIA published current driver guidance.",
          "annotations": [
            {
              "type": "url_citation",
              "start_index": 0,
              "end_index": 6,
              "url": "https://www.nvidia.com/download/index.aspx",
              "title": "NVIDIA Driver Downloads"
            }
          ]
        }
      ]
    }
  ],
  "usage": { "input_tokens": 120, "output_tokens": 24 }
}
""";
using (JsonDocument providerDocument = JsonDocument.Parse(providerResponse))
{
    AiProviderParsedResponse parsed = AiProviderResponseParser.Parse(providerDocument.RootElement);
    Check(parsed.Answer == "NVIDIA published current driver guidance.",
        "Provider response parser extracts the answer text");
    Check(parsed.InputTokens == 120 && parsed.OutputTokens == 24,
        "Provider response parser preserves token usage");
    Check(parsed.UsedWebSearch,
        "Provider response parser detects web-search use");
    Check(parsed.Citations.Count == 1 &&
          parsed.Citations[0].StartIndex == 0 && parsed.Citations[0].EndIndex == 6 &&
          parsed.Citations[0].Url.StartsWith("https://www.nvidia.com/", StringComparison.OrdinalIgnoreCase),
        "Provider response parser accepts a bounded HTTPS inline citation");
    Check(parsed.Sources.Count == 1 &&
          parsed.Sources[0].Url.StartsWith("https://www.nvidia.com/", StringComparison.OrdinalIgnoreCase),
        "Provider response parser returns a deduplicated attributable HTTPS source");
}

const string hostileProviderResponse = """
{
  "output": [
    {
      "type": "web_search_call",
      "action": {
        "sources": [
          { "url": "http://insecure.example/source", "title": "Insecure" }
        ]
      }
    },
    {
      "type": "message",
      "content": [
        {
          "type": "output_text",
          "text": "Short answer.",
          "annotations": [
            { "type": "url_citation", "start_index": 0, "end_index": 999, "url": "https://example.com/out-of-range", "title": "Bad Range" },
            { "type": "url_citation", "start_index": 0, "end_index": 5, "url": "http://example.com/insecure", "title": "HTTP" }
          ]
        }
      ]
    }
  ],
  "usage": { "input_tokens": 20, "output_tokens": 5 }
}
""";
using (JsonDocument hostileDocument = JsonDocument.Parse(hostileProviderResponse))
{
    AiProviderParsedResponse parsed = AiProviderResponseParser.Parse(hostileDocument.RootElement);
    Check(parsed.UsedWebSearch,
        "Web-search tool use is detected even when returned source metadata is rejected");
    Check(parsed.Citations.Count == 0,
        "Out-of-range and non-HTTPS citations fail closed");
    Check(parsed.Sources.Count == 0,
        "Non-HTTPS web-search sources fail closed");
}

using (StringContent validJson = new("{\"ok\":true}", Encoding.UTF8, "application/json"))
using (var validDocument = await BoundedHttpJson.TryReadAsync(validJson, 1024, 8, CancellationToken.None))
{
    Check(validDocument?.RootElement.TryGetProperty("ok", out var ok) == true && ok.GetBoolean(), "Bounded upstream JSON accepts valid bounded content");
}

using (ByteArrayContent declaredOversized = new(new byte[2048]))
{
    declaredOversized.Headers.ContentLength = 2048;
    using var oversizedDocument = await BoundedHttpJson.TryReadAsync(declaredOversized, 1024, 8, CancellationToken.None);
    Check(oversizedDocument is null, "Declared oversized upstream JSON is rejected before parse");
}

using (var streamedOversized = new StreamContent(new NonSeekableRepeatingStream((byte)'A', 2048)))
{
    using var oversizedDocument = await BoundedHttpJson.TryReadAsync(streamedOversized, 1024, 8, CancellationToken.None);
    Check(oversizedDocument is null, "Chunked oversized upstream JSON is rejected while streaming");
}

using (ByteArrayContent malformed = new(Encoding.UTF8.GetBytes("{not-json}")))
{
    using var malformedDocument = await BoundedHttpJson.TryReadAsync(malformed, 1024, 8, CancellationToken.None);
    Check(malformedDocument is null, "Malformed upstream JSON fails closed");
}

using (var stalled = new StreamContent(new BlockingReadStream()))
{
    DateTimeOffset started = DateTimeOffset.UtcNow;
    using var stalledDocument = await BoundedHttpJson.TryReadAsync(
        stalled,
        1024,
        8,
        TimeSpan.FromMilliseconds(100),
        CancellationToken.None);
    TimeSpan elapsed = DateTimeOffset.UtcNow - started;
    Check(stalledDocument is null && elapsed < TimeSpan.FromSeconds(2), "Stalled upstream body is bounded by one wall-clock deadline");
}

using (var faulted = new StreamContent(new FaultingReadStream()))
{
    using var faultedDocument = await BoundedHttpJson.TryReadAsync(
        faulted,
        1024,
        8,
        TimeSpan.FromSeconds(1),
        CancellationToken.None);
    Check(faultedDocument is null, "Mid-body upstream I/O failure fails closed without escaping the bounded reader");
}

Environment.SetEnvironmentVariable("SENTINEL_AI_MAX_REPLAY_ENTRIES", "100");
var boundedReplay = new GatewaySecurity(new FakeHttpClientFactory());
string? replayCapacityToken = boundedReplay.IssueSession("Basic", "capacity-test");
bool acceptedToCapacity = !string.IsNullOrWhiteSpace(replayCapacityToken);
if (acceptedToCapacity)
{
    for (int i = 0; i < 100; i++)
    {
        SessionValidationResult accepted = boundedReplay.ValidateSession(
            "Bearer " + replayCapacityToken,
            Guid.NewGuid().ToString(),
            "Basic");
        if (!accepted.Authorized)
        {
            acceptedToCapacity = false;
            break;
        }
    }
}
Check(acceptedToCapacity, "Replay cache accepts requests through its configured capacity");
SessionValidationResult replayCapacity = boundedReplay.ValidateSession(
    "Bearer " + replayCapacityToken,
    Guid.NewGuid().ToString(),
    "Basic");
Check(!replayCapacity.Available && !replayCapacity.Authorized && replayCapacity.Reason.Contains("capacity", StringComparison.OrdinalIgnoreCase),
    "Replay cache fails closed at configured capacity");
Environment.SetEnvironmentVariable("SENTINEL_AI_MAX_REPLAY_ENTRIES", null);

Environment.SetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY", "too-short");
var misconfigured = new GatewaySecurity(new FakeHttpClientFactory());
Check(misconfigured.IssueSession("Basic", "test") is null, "Weak signing key fails closed");

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
return failures == 0 ? 0 : 1;

sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new RejectNetworkHandler()) { BaseAddress = new Uri("https://invalid.local/") };
}

sealed class RejectNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
}

sealed class NonSeekableRepeatingStream : Stream
{
    private readonly byte _value;
    private int _remaining;

    internal NonSeekableRepeatingStream(byte value, int length)
    {
        _value = value;
        _remaining = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0) return 0;
        int read = Math.Min(count, _remaining);
        Array.Fill(buffer, _value, offset, read);
        _remaining -= read;
        return read;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_remaining <= 0) return ValueTask.FromResult(0);
        int read = Math.Min(buffer.Length, _remaining);
        buffer.Span[..read].Fill(_value);
        _remaining -= read;
        return ValueTask.FromResult(read);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

sealed class BlockingReadStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

sealed class FaultingReadStream : Stream
{
    private bool _returnedPrefix;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_returnedPrefix)
        {
            _returnedPrefix = true;
            byte[] prefix = Encoding.UTF8.GetBytes("{\"partial\":");
            prefix.AsSpan().CopyTo(buffer.Span);
            return ValueTask.FromResult(prefix.Length);
        }

        return ValueTask.FromException<int>(new IOException("Injected upstream body failure."));
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
