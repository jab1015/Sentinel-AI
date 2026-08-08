using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Maintenance History Acceptance ===");

string root = Path.Combine(
    Path.GetTempPath(),
    $"sentinel-maintenance-history-{Guid.NewGuid():N}");
string historyPath = Path.Combine(root, "maintenance-history.json");
Directory.CreateDirectory(root);

try
{
    MaintenanceHistoryService service = new(historyPath);

    MaintenanceHistorySummary empty = service.GetSummary();
    bool missingIsAvailableAndEmpty =
        empty.HistoryAvailable && empty.TotalActions == 0;
    Console.WriteLine($"Missing history is available and empty: {missingIsAvailableAndEmpty}");

    service.Record(new MaintenanceHistoryEntry(
        DateTimeOffset.UtcNow,
        "Optimization",
        "Temporary file cleanup",
        "Sentinel removed stale temporary files and verified the result.",
        Attempted: true,
        Successful: true,
        Verified: true,
        RolledBack: false,
        TechnicalDetail: string.Empty));

    MaintenanceHistorySummary recorded = service.GetSummary();
    bool verifiedRecordRetained =
        recorded.HistoryAvailable &&
        recorded.TotalActions == 1 &&
        recorded.VerifiedActions == 1 &&
        recorded.Entries[0].Category == "Optimization";
    Console.WriteLine($"Verified optimization record retained: {verifiedRecordRetained}");

    const string corruptContent = "{not-valid-json";
    File.WriteAllText(historyPath, corruptContent);
    MaintenanceHistorySummary corrupt = service.GetSummary();
    bool corruptIsUnavailable =
        !corrupt.HistoryAvailable &&
        corrupt.TotalActions == 0 &&
        corrupt.Summary.Contains("could not read", StringComparison.OrdinalIgnoreCase);
    Console.WriteLine($"Corrupt history is unavailable, not empty: {corruptIsUnavailable}");

    service.Record(new MaintenanceHistoryEntry(
        DateTimeOffset.UtcNow,
        "Maintenance",
        "Should not overwrite",
        "This entry must not replace unreadable history.",
        true,
        true,
        true,
        false,
        string.Empty));

    bool corruptFilePreserved =
        File.ReadAllText(historyPath) == corruptContent;
    Console.WriteLine($"Unreadable history is not overwritten: {corruptFilePreserved}");

    bool pass =
        missingIsAvailableAndEmpty &&
        verifiedRecordRetained &&
        corruptIsUnavailable &&
        corruptFilePreserved;

    Console.WriteLine();
    Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
    Environment.ExitCode = pass ? 0 : 1;
}
finally
{
    try { Directory.Delete(root, recursive: true); }
    catch { }
}
