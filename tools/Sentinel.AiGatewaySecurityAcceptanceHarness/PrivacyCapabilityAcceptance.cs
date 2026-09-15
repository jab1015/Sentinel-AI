using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class PrivacyCapabilityAcceptance
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        Console.WriteLine("--- PrivacyCapabilityAcceptance: START ---");
        string? prior = Environment.GetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY");
        const string keyText = "privacy-capability-acceptance-signing-key-0123456789-ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        Environment.SetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY", keyText);
        try
        {
            const string subject = "store:0123456789ABCDEF01234567";
            PrivacyCapabilitySecurity security = new();
            Require(security.UsesProcessLocalReplayState, "Replay-state deployment limitation must remain explicit until a shared store is implemented.");

            string[] scopes = { "privacy.encrypt", "privacy.vault", "privacy.secure-delete", "privacy.discovery" };
            foreach (string scope in scopes)
            {
                PrivacyCapabilityIssueResult issued = security.Issue(subject, scope);
                Require(issued.Available && issued.Authorized && !string.IsNullOrWhiteSpace(issued.Token), scope + " was not issued.");
                Require(issued.ExpiresInSeconds == 45, "Privacy capability lifetime drifted from the short-lived policy.");
                PrivacyCapabilityValidationResult accepted = security.ValidateAndConsume(issued.Token, scope, subject);
                Require(accepted.Available && accepted.Authorized && accepted.Scope == scope, scope + " was not validated.");
                PrivacyCapabilityValidationResult replay = security.ValidateAndConsume(issued.Token, scope, subject);
                Require(!replay.Authorized && replay.Reason.Contains("already", StringComparison.OrdinalIgnoreCase), scope + " replay was accepted.");
            }

            PrivacyCapabilityIssueResult wrongFeatureFixture = security.Issue(subject, "privacy.encrypt");
            Require(wrongFeatureFixture.Authorized, "Wrong-feature fixture could not be issued.");
            Require(!security.ValidateAndConsume(wrongFeatureFixture.Token, "privacy.secure-delete", subject).Authorized,
                "Capability for the wrong privacy feature was accepted.");
            Require(security.ValidateAndConsume(wrongFeatureFixture.Token, "privacy.encrypt", subject).Authorized,
                "Wrong-feature validation consumed a capability that should remain valid for its own scope.");

            PrivacyCapabilityIssueResult subjectFixture = security.Issue(subject, "privacy.discovery");
            Require(!security.ValidateAndConsume(subjectFixture.Token, "privacy.discovery", "store:DIFFERENT").Authorized,
                "Capability was accepted for a different Store subject.");
            Require(security.ValidateAndConsume(subjectFixture.Token, "privacy.discovery", subject).Authorized,
                "Subject-mismatch validation consumed a valid capability.");

            PrivacyCapabilityIssueResult tamperFixture = security.Issue(subject, "privacy.vault");
            string tampered = TamperSignature(tamperFixture.Token);
            Require(!security.ValidateAndConsume(tampered, "privacy.vault", subject).Authorized,
                "Tampered privacy capability was accepted.");

            PrivacyCapabilityIssueResult expiryFixture = security.Issue(subject, "privacy.encrypt");
            string expired = ForgeExpired(expiryFixture.Token, keyText);
            Require(!security.ValidateAndConsume(expired, "privacy.encrypt", subject).Authorized,
                "Expired privacy capability was accepted.");

            Require(!security.Issue(subject, "privacy.admin").Authorized, "Unknown privacy capability scope was issued.");
            Require(!security.ValidateAndConsume("malformed", "privacy.encrypt", subject).Authorized,
                "Malformed privacy capability was accepted.");

            string? savedKey = Environment.GetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY");
            Environment.SetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY", null);
            Require(!new PrivacyCapabilitySecurity().Issue(subject, "privacy.encrypt").Available,
                "Capability issuance did not fail closed when server signing configuration was unavailable.");
            Environment.SetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY", savedKey);

            Console.WriteLine("Feature-scoped issue/validation: PASS");
            Console.WriteLine("Wrong feature / subject / tamper / malformed rejection: PASS");
            Console.WriteLine("One-time replay rejection: PASS");
            Console.WriteLine("Expired capability rejection: PASS");
            Console.WriteLine("Backend signing unavailable fail-closed: PASS");
            Console.WriteLine("Process-local replay state surfaced as staging blocker: PASS");
            Console.WriteLine("--- PrivacyCapabilityAcceptance: COMPLETE ---");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY", prior);
        }
    }

    private static string TamperSignature(string token)
    {
        string[] parts = token.Split('.');
        Require(parts.Length == 2, "Tamper fixture token format was unexpected.");
        byte[] signature = Base64UrlDecode(parts[1]);
        Require(signature.Length > 0, "Tamper fixture signature was empty.");

        // Mutate an authenticated byte rather than a Base64URL character. Changing the
        // final encoded character can alter only unused padding bits and decode back to
        // the exact same signature, making the fixture nondeterministic.
        signature[0] ^= 0x01;
        return parts[0] + "." + Base64UrlEncode(signature);
    }

    private static string ForgeExpired(string token, string keyText)
    {
        string[] parts = token.Split('.');
        byte[] payload = Base64UrlDecode(parts[0]);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        object expiredPayload = new
        {
            Version = root.GetProperty("Version").GetInt32(),
            Subject = root.GetProperty("Subject").GetString(),
            Scope = root.GetProperty("Scope").GetString(),
            IssuedUnixSeconds = now - 120,
            ExpiresUnixSeconds = now - 60,
            TokenId = root.GetProperty("TokenId").GetString()
        };
        string encoded = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(expiredPayload)));
        byte[] signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(keyText), Encoding.ASCII.GetBytes(encoded));
        return encoded + "." + Base64UrlEncode(signature);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", 0 => string.Empty, _ => throw new FormatException() };
        return Convert.FromBase64String(padded);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
