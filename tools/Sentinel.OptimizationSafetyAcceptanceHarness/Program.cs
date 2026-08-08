using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Optimization Safety Acceptance ===");

string root = Path.Combine(
    Path.GetTempPath(),
    $"sentinel-optimization-safety-{Guid.NewGuid():N}");
string settingsPath = Path.Combine(root, "optimization-settings.json");
Directory.CreateDirectory(root);

try
{
    OptimizationSettingsService service = new(settingsPath);

    OptimizationSettings missing = service.Load();
    bool defaultOff = !missing.AutomaticOptimizationEnabled;
    bool defaultSafety = missing.VerifyEveryChange && missing.RollBackWhenPossible;
    Console.WriteLine($"Fresh settings default off: {defaultOff}");
    Console.WriteLine($"Fresh settings require verification/rollback: {defaultSafety}");

    OptimizationSettings unsafeRequest = new(
        AutomaticOptimizationEnabled: true,
        Mode: (OptimizationMode)999,
        VerifyEveryChange: false,
        RollBackWhenPossible: false);

    bool saved = service.Save(unsafeRequest);
    OptimizationSettings normalized = service.Load();
    bool explicitOptInPreserved = normalized.AutomaticOptimizationEnabled;
    bool invalidModeConservative = normalized.Mode == OptimizationMode.Conservative;
    bool mandatorySafety = normalized.VerifyEveryChange && normalized.RollBackWhenPossible;
    bool noTemporaryFiles = Directory
        .EnumerateFiles(root, ".optimization-settings.*.tmp")
        .Any() == false;

    Console.WriteLine($"Atomic save succeeded: {saved}");
    Console.WriteLine($"Explicit opt-in preserved: {explicitOptInPreserved}");
    Console.WriteLine($"Invalid mode normalized to Conservative: {invalidModeConservative}");
    Console.WriteLine($"Unsafe verification/rollback request rejected: {mandatorySafety}");
    Console.WriteLine($"No temporary settings files remain: {noTemporaryFiles}");

    File.WriteAllText(settingsPath, "{not-valid-json");
    OptimizationSettings corrupt = service.Load();
    bool corruptFailsClosed =
        !corrupt.AutomaticOptimizationEnabled &&
        corrupt.Mode == OptimizationMode.Conservative &&
        corrupt.VerifyEveryChange &&
        corrupt.RollBackWhenPossible;
    Console.WriteLine($"Corrupt settings fail closed: {corruptFailsClosed}");

    bool pass =
        defaultOff &&
        defaultSafety &&
        saved &&
        explicitOptInPreserved &&
        invalidModeConservative &&
        mandatorySafety &&
        noTemporaryFiles &&
        corruptFailsClosed;

    Console.WriteLine();
    Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
    Environment.ExitCode = pass ? 0 : 1;
}
finally
{
    try { Directory.Delete(root, recursive: true); }
    catch { }
}
