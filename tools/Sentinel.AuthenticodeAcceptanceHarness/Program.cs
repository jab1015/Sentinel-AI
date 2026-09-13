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

    Console.WriteLine();
    Console.WriteLine("--- Scenario 5: verification lease blocks path/object replacement ---");
    string leaseTarget = Path.Combine(tempRoot, "lease-target.exe");
    string replacement = Path.Combine(tempRoot, "lease-replacement.exe");
    File.Copy(trustedPath, leaseTarget, overwrite: true);
    File.WriteAllText(replacement, "attacker replacement");

    bool replacementBlocked;
    bool writeBlocked;
    bool deleteBlocked;
    using (FileStream lease = AuthenticodeVerifier.OpenVerificationLease(leaseTarget))
    {
        try
        {
            File.Move(replacement, leaseTarget, overwrite: true);
            replacementBlocked = false;
        }
        catch (IOException)
        {
            replacementBlocked = true;
        }
        catch (UnauthorizedAccessException)
        {
            replacementBlocked = true;
        }

        try
        {
            using FileStream writer = new(leaseTarget, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            writer.WriteByte(0x41);
            writer.Flush(true);
            writeBlocked = false;
        }
        catch (IOException)
        {
            writeBlocked = true;
        }
        catch (UnauthorizedAccessException)
        {
            writeBlocked = true;
        }

        try
        {
            File.Delete(leaseTarget);
            deleteBlocked = false;
        }
        catch (IOException)
        {
            deleteBlocked = true;
        }
        catch (UnauthorizedAccessException)
        {
            deleteBlocked = true;
        }
    }

    bool leasePass = replacementBlocked && writeBlocked && deleteBlocked && File.Exists(leaseTarget);
    Console.WriteLine($"Stable verification lease: {(leasePass ? "PASS" : "FAIL")} (replace={replacementBlocked}, write={writeBlocked}, delete={deleteBlocked})");

    bool pass = trustedPass && unsignedPass && tamperedPass && mappingPass && leasePass;
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
