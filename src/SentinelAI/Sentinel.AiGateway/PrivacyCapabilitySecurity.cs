using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed class PrivacyCapabilitySecurity
{
    internal static readonly TimeSpan CapabilityLifetime = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ReplayRetention = TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> AllowedScopes = new(StringComparer.Ordinal)
    {
        "privacy.encrypt",
        "privacy.vault",
        "privacy.secure-delete",
        "privacy.discovery"
    };

    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumed = new(StringComparer.Ordinal);
    private readonly object _replayGate = new();
    private readonly int _maximumReplayEntries;

    internal PrivacyCapabilitySecurity()
    {
        _maximumReplayEntries = ReadInt("SENTINEL_PRIVACY_MAX_REPLAY_ENTRIES", 25_000, 100, 250_000);
    }

    internal bool UsesProcessLocalReplayState => true;

    internal PrivacyCapabilityIssueResult Issue(string subject, string scope)
    {
        if (!TryNormalizeScope(scope, out string normalizedScope))
            return PrivacyCapabilityIssueResult.Denied("The requested privacy capability scope is unsupported.");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 160)
            return PrivacyCapabilityIssueResult.Denied("The privacy capability subject is invalid.");

        byte[]? key = ReadSigningKey();
        if (key is null)
            return PrivacyCapabilityIssueResult.Unavailable("Gateway capability signing is not configured.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        PrivacyCapabilityPayload payload = new(
            Version: 1,
            Subject: subject,
            Scope: normalizedScope,
            IssuedUnixSeconds: now.ToUnixTimeSeconds(),
            ExpiresUnixSeconds: now.Add(CapabilityLifetime).ToUnixTimeSeconds(),
            TokenId: Guid.NewGuid().ToString("N"));

        string encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        byte[] signature = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(encodedPayload));
        return PrivacyCapabilityIssueResult.Success(
            encodedPayload + "." + Base64UrlEncode(signature),
            normalizedScope,
            (int)CapabilityLifetime.TotalSeconds);
    }

    internal PrivacyCapabilityValidationResult ValidateAndConsume(
        string? token,
        string scope,
        string expectedSubject)
    {
        if (!TryNormalizeScope(scope, out string normalizedScope))
            return PrivacyCapabilityValidationResult.Denied("The requested privacy capability scope is unsupported.");
        if (string.IsNullOrWhiteSpace(expectedSubject) || expectedSubject.Length > 160)
            return PrivacyCapabilityValidationResult.Denied("The expected privacy capability subject is invalid.");
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16_384)
            return PrivacyCapabilityValidationResult.Denied("The privacy capability is missing or invalid.");

        string[] parts = token.Split('.');
        if (parts.Length != 2)
            return PrivacyCapabilityValidationResult.Denied("The privacy capability is malformed.");

        byte[]? key = ReadSigningKey();
        if (key is null)
            return PrivacyCapabilityValidationResult.Unavailable("Gateway capability signing is not configured.");

        byte[] payloadBytes;
        byte[] providedSignature;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            providedSignature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return PrivacyCapabilityValidationResult.Denied("The privacy capability encoding is invalid.");
        }

        byte[] expectedSignature = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(parts[0]));
        if (providedSignature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
            return PrivacyCapabilityValidationResult.Denied("The privacy capability signature is invalid.");

        PrivacyCapabilityPayload? payload;
        try { payload = JsonSerializer.Deserialize<PrivacyCapabilityPayload>(payloadBytes); }
        catch (JsonException) { return PrivacyCapabilityValidationResult.Denied("The privacy capability payload is invalid."); }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (payload is null || payload.Version != 1 ||
            string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.TokenId) ||
            payload.IssuedUnixSeconds <= 0 || payload.ExpiresUnixSeconds <= payload.IssuedUnixSeconds ||
            payload.ExpiresUnixSeconds <= now || payload.IssuedUnixSeconds > now + 60 ||
            !string.Equals(payload.Subject, expectedSubject, StringComparison.Ordinal) ||
            !string.Equals(payload.Scope, normalizedScope, StringComparison.Ordinal) ||
            !Guid.TryParseExact(payload.TokenId, "N", out _))
        {
            return PrivacyCapabilityValidationResult.Denied("The privacy capability is expired, mismatched, or invalid.");
        }

        lock (_replayGate)
        {
            PruneLocked();
            if (_consumed.ContainsKey(payload.TokenId))
                return PrivacyCapabilityValidationResult.Denied("The privacy capability was already consumed.");
            if (_consumed.Count >= _maximumReplayEntries)
                return PrivacyCapabilityValidationResult.Unavailable("Privacy capability replay protection is at capacity and failed closed.");
            if (!_consumed.TryAdd(payload.TokenId, DateTimeOffset.UtcNow.Add(ReplayRetention)))
                return PrivacyCapabilityValidationResult.Denied("The privacy capability was already consumed.");
        }

        return PrivacyCapabilityValidationResult.Allowed(payload.Subject, payload.Scope, payload.TokenId);
    }

    private void PruneLocked()
    {
        if (_consumed.Count < Math.Min(1_000, _maximumReplayEntries)) return;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach ((string key, DateTimeOffset expires) in _consumed)
            if (expires <= now) _consumed.TryRemove(key, out _);
    }

    private static bool TryNormalizeScope(string? scope, out string normalized)
    {
        normalized = scope?.Trim().ToLowerInvariant() ?? string.Empty;
        return AllowedScopes.Contains(normalized);
    }

    private static byte[]? ReadSigningKey()
    {
        string? raw = Environment.GetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY")?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        byte[] key = Encoding.UTF8.GetBytes(raw);
        return key.Length >= 32 ? key : null;
    }

    private static int ReadInt(string name, int fallback, int min, int max) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? Math.Clamp(value, min, max) : fallback;

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", 0 => string.Empty, _ => throw new FormatException() };
        return Convert.FromBase64String(padded);
    }

    private sealed record PrivacyCapabilityPayload(
        int Version,
        string Subject,
        string Scope,
        long IssuedUnixSeconds,
        long ExpiresUnixSeconds,
        string TokenId);
}

internal sealed record PrivacyCapabilityIssueResult(
    bool Available,
    bool Authorized,
    string Token,
    string Scope,
    int ExpiresInSeconds,
    string Reason)
{
    internal static PrivacyCapabilityIssueResult Success(string token, string scope, int expiresInSeconds) =>
        new(true, true, token, scope, expiresInSeconds, string.Empty);
    internal static PrivacyCapabilityIssueResult Denied(string reason) =>
        new(true, false, string.Empty, string.Empty, 0, reason);
    internal static PrivacyCapabilityIssueResult Unavailable(string reason) =>
        new(false, false, string.Empty, string.Empty, 0, reason);
}

internal sealed record PrivacyCapabilityValidationResult(
    bool Available,
    bool Authorized,
    string Subject,
    string Scope,
    string TokenId,
    string Reason)
{
    internal static PrivacyCapabilityValidationResult Allowed(string subject, string scope, string tokenId) =>
        new(true, true, subject, scope, tokenId, string.Empty);
    internal static PrivacyCapabilityValidationResult Denied(string reason) =>
        new(true, false, string.Empty, string.Empty, string.Empty, reason);
    internal static PrivacyCapabilityValidationResult Unavailable(string reason) =>
        new(false, false, string.Empty, string.Empty, string.Empty, reason);
}
