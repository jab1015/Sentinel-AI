using Sentinel.App.Services;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string root = Path.Combine(Path.GetTempPath(), "SentinelAI-HistoryAcceptance-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string path = Path.Combine(root, "investigations.jsonl");
try
{
    var service = new InvestigationHistoryService(path);

    Console.WriteLine("=== Sentinel AI Investigation History Acceptance ===");

    for (int i = 0; i < 12; i++)
    {
        await service.RecordAsync($"fingerprint-{i}", $"Title {i}", $"Conclusion {i}", "Information", false, false);
    }

    var recentFive = await service.ReadRecentAsync(5);
    Require(recentFive.Count == 5, $"Expected 5 recent entries, got {recentFive.Count}.");
    Require(recentFive[0].Fingerprint == "fingerprint-11" && recentFive[^1].Fingerprint == "fingerprint-7", "Tail read did not return newest entries in reverse chronological file order.");
    Console.WriteLine("Bounded recent tail read: PASS");

    string largeConclusion = new string('X', 20_000);
    for (int i = 0; i < 460; i++)
    {
        await service.RecordAsync($"rotation-{i}", $"Rotation {i}", largeConclusion + i, "Information", false, false);
    }

    long length = new FileInfo(path).Length;
    Require(length <= 8L * 1024 * 1024, $"History file exceeded 8 MiB retention bound: {length} bytes.");
    Console.WriteLine($"Rotation bound: PASS ({length} bytes)");

    var recentTen = await service.ReadRecentAsync(10);
    Require(recentTen.Count == 10, $"Expected 10 entries after rotation, got {recentTen.Count}.");
    Require(recentTen[0].Fingerprint == "rotation-459", "Newest entry was not retained after rotation.");
    Console.WriteLine("Newest entries retained after rotation: PASS");

    await File.AppendAllTextAsync(path, "{not-json}" + Environment.NewLine);
    var withCorruptTail = await service.ReadRecentAsync(5);
    Require(withCorruptTail.Count > 0 && withCorruptTail.All(x => x.Fingerprint.StartsWith("rotation-", StringComparison.Ordinal)), "Corrupt tail record prevented recovery of valid recent entries.");
    Console.WriteLine("Corrupt tail record skipped safely: PASS");

    var capped = await service.ReadRecentAsync(50_000);
    Require(capped.Count <= 2_000, $"Recent-entry caller cap was not enforced: {capped.Count}.");
    Console.WriteLine("Caller entry cap: PASS");

    Console.WriteLine("RESULT: PASS");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}
