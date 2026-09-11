using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Authenticode Acceptance ===");

string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
string trustedPath = Path.Combine(windowsDirectory, "System32", "notepad.exe");
string? harnessPath = Environment.ProcessPath;
string tempRoot = Path.Combine(Path.GetTempPath(), "SentinelAI-AuthenticodeAcceptance", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    Console.WriteLine();
    Console.WriteLine("--- Scenario 1: trusted Windows executable ---");
    AuthenticodeVerificationResult trusted = AuthenticodeVerifier.Verify(trustedPath);
    bool trustedPass = trusted.IsTrusted &&
        (trusted.Status == AuthenticodeTrustStatus.Trusted || trusted.Status == AuthenticodeTrustStatus.TrustedTimestamped);
    Console.WriteLine($"Trusted Windows executable: {(trustedPass ? "PASS" : "FAIL")} ({trusted.Status}, {trusted.Publisher})");

    Console.WriteLine();
    Console.WriteLine("--- Scenario 2: unsigned harness executable ---");
    AuthenticodeVerificationResult unsigned = harnessPath is null
        ? new(AuthenticodeTrustStatus.VerificationError, false, false, "Unknown", "Harness path unavailable")
        : AuthenticodeVerifier.Verify(harnessPath);
    bool unsignedPass = unsigned.Status == AuthenticodeTrustStatus.Unsigned || !unsigned.IsTrusted;
    Console.WriteLine($"Unsigned/untrusted executable: {(unsignedPass ? "PASS" : "FAIL")} ({unsigned.Status})");

    Console.WriteLine();
    Console.WriteLine("--- Scenario 3: tampered trusted executable ---");
    string tamperedPath = Path.Combine(tempRoot, "notepad-tampered.exe");
    File.Copy(trustedPath, tamperedPath, overwrite: true);
    await using (FileStream stream = new(tamperedPath, FileMode.Append, FileAccess.Write, FileShare.None))
    {
        await stream.WriteAsync(new byte[] { 0x53, 0x41, 0x49 });
        await stream.FlushAsync();
    }
    AuthenticodeVerificationResult tampered = AuthenticodeVerifier.Verify(tamperedPath);
    bool tamperedPass = !tampered.IsTrusted && tampered.Status != AuthenticodeTrustStatus.Trusted && tampered.Status != AuthenticodeTrustStatus.TrustedTimestamped;
    Console.WriteLine($"Tampered executable rejected: {(tamperedPass ? "PASS" : "FAIL")} ({tampered.Status})");

    Console.WriteLine();
    Console.WriteLine("--- Scenario 4: explicit trust-result mapping ---");
    bool mappingPass =
        AuthenticodeVerifier.MapStatus(unchecked((int)0x80096010), "Test Publisher").Status == AuthenticodeTrustStatus.ModifiedAfterSigning &&
        AuthenticodeVerifier.MapStatus(unchecked((int)0x800B010C), "Test Publisher").Status == AuthenticodeTrustStatus.Revoked &&
        AuthenticodeVerifier.MapStatus(unchecked((int)0x800B0101), "Test Publisher").Status == AuthenticodeTrustStatus.Expired &&
        AuthenticodeVerifier.MapStatus(unchecked((int)0x800B0004), "Test Publisher").Status == AuthenticodeTrustStatus.UntrustedSigner &&
        AuthenticodeVerifier.MapStatus(0, "Test Publisher", signerCertificateExpired: true).Status == AuthenticodeTrustStatus.TrustedTimestamped;
    Console.WriteLine($"Structured status mapping: {(mappingPass ? "PASS" : "FAIL")}");

    bool pass = trustedPass && unsignedPass && tamperedPass && mappingPass;
    Console.WriteLine();
    Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
    Environment.ExitCode = pass ? 0 : 1;
}
finally
{
    try
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
    catch
    {
        // Test cleanup must not alter the result.
    }
}
