using Sentinel.App.Services;

static void RequireRedacted(string input, params string[] forbidden)
{
    string output = AiEvidencePackageBuilder.SanitizeForCloud(input);
    foreach (string value in forbidden)
        if (output.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Sensitive value survived redaction: {value}. Output: {output}");
}

Console.WriteLine("=== Sentinel AI Cloud Redaction Acceptance ===");

RequireRedacted("Authorization: Bearer abc.def.ghi", "abc.def.ghi");
Console.WriteLine("Authorization header: PASS");

RequireRedacted("token=super-secret-token; next=value", "super-secret-token");
RequireRedacted("client_secret='client-secret-value'", "client-secret-value");
RequireRedacted("password=DontSendThis123", "DontSendThis123");
Console.WriteLine("Named credentials: PASS");

string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijklmno";
RequireRedacted($"session {jwt}", jwt);
Console.WriteLine("JWT-like token: PASS");

RequireRedacted("user=jane.doe account=corp-user jane.doe@example.com", "jane.doe", "corp-user", "jane.doe@example.com");
Console.WriteLine("User/account/email identifiers: PASS");

RequireRedacted("adapter 00-11-22-33-44-55 device-id=ABC123 serial number=ZXCV987", "00-11-22-33-44-55", "ABC123", "ZXCV987");
Console.WriteLine("MAC/device identifiers: PASS");

RequireRedacted("remote=192.168.1.55 ipv6=2001:db8::1234", "192.168.1.55", "2001:db8::1234");
Console.WriteLine("IP literals: PASS");

RequireRedacted(@"path C:\Users\Alice\Documents\report.txt", "Alice");
Console.WriteLine("User profile path: PASS");

string benign = AiEvidencePackageBuilder.SanitizeForCloud("Windows Defender status is enabled; port 443 is active.");
if (!benign.Contains("Windows Defender status is enabled", StringComparison.Ordinal))
    throw new InvalidOperationException("Benign evidence was destroyed by redaction.");
Console.WriteLine("Benign evidence preservation: PASS");

Console.WriteLine("RESULT: PASS");
