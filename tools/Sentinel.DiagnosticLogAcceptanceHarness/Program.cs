using Sentinel.App.Services;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string root = Path.Combine(Path.GetTempPath(), "SentinelAI-LogAcceptance-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var log = new DiagnosticLogService(root);
    Console.WriteLine("=== Sentinel AI Diagnostic Log Acceptance ===");

    await log.ErrorAsync(
        "SensitiveEvent",
        @"Authorization=secret-token Bearer abc.def.ghi C:\Users\Alice\Desktop\file.txt",
        new InvalidOperationException("password=DontLogThis", new Exception("token=NestedSecret")));

    string text = await File.ReadAllTextAsync(log.LogPath);
    Require(!text.Contains("secret-token", StringComparison.OrdinalIgnoreCase), "Authorization value survived log redaction.");
    Require(!text.Contains("abc.def.ghi", StringComparison.OrdinalIgnoreCase), "Bearer value survived log redaction.");
    Require(!text.Contains("Alice", StringComparison.OrdinalIgnoreCase), "User profile name survived log redaction.");
    Require(!text.Contains("DontLogThis", StringComparison.OrdinalIgnoreCase), "Password survived exception redaction.");
    Require(!text.Contains("NestedSecret", StringComparison.OrdinalIgnoreCase), "Nested token survived exception redaction.");
    Require(text.Contains("InvalidOperationException", StringComparison.Ordinal) && text.Contains("inner:", StringComparison.OrdinalIgnoreCase), "Exception type/inner diagnostic context was not preserved.");
    Console.WriteLine("Sensitive diagnostic redaction: PASS");

    log.WriteCrashBreadcrumb("CrashBoundary", new InvalidOperationException("api_key=CrashSecret"));
    Require(File.Exists(log.CrashBreadcrumbPath), "Crash breadcrumb was not written synchronously.");
    string crash = await File.ReadAllTextAsync(log.CrashBreadcrumbPath);
    Require(crash.Contains("CRASH", StringComparison.Ordinal) && crash.Contains("InvalidOperationException", StringComparison.Ordinal), "Crash breadcrumb omitted bounded failure context.");
    Require(!crash.Contains("CrashSecret", StringComparison.OrdinalIgnoreCase), "Crash breadcrumb leaked credential material.");
    Console.WriteLine("Synchronous crash breadcrumb: PASS");

    string payload = new string('L', 15_000);
    for (int i = 0; i < 150; i++) await log.InformationAsync("Rotation", payload + i);
    Require(File.Exists(log.PreviousLogPath), "Log rotation did not preserve the previous bounded log.");
    long currentLength = new FileInfo(log.LogPath).Length;
    long previousLength = new FileInfo(log.PreviousLogPath).Length;
    Require(currentLength <= 2L * 1024 * 1024 + 20_000, $"Current log grew unexpectedly beyond the rotation boundary: {currentLength}.");
    Require(previousLength >= 2L * 1024 * 1024, "Previous log does not contain the rotated bounded log segment.");
    Console.WriteLine($"Bounded log rotation: PASS (current={currentLength}, previous={previousLength})");

    Console.WriteLine("RESULT: PASS");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}
