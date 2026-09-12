using Sentinel.App.Services;
using System.Text.Json;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string RecordJson(string command, long createdUnixMs, params string[] items) =>
    JsonSerializer.Serialize(new
    {
        Version = ExplorerHandoffService.ProtocolVersion,
        Command = command,
        CreatedUnixMs = createdUnixMs,
        Items = items
    });

static void WriteRecord(string root, Guid id, string json)
{
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, id.ToString("N") + ".json"), json);
}

Console.WriteLine("=== Sentinel Explorer Integration Acceptance ===");

Guid parsedId = Guid.NewGuid();
Require(
    ExplorerHandoffService.TryParseHandoffId(
        new[] { ExplorerHandoffService.InspectArgument, parsedId.ToString("N") },
        out Guid parsed) && parsed == parsedId,
    "Exact Explorer handoff arguments were not accepted.");
Require(
    ExplorerHandoffService.TryParseHandoffId(
        $"{ExplorerHandoffService.InspectArgument} {parsedId:N}",
        out parsed) && parsed == parsedId,
    "Launch argument string was not parsed.");
Require(
    !ExplorerHandoffService.TryParseHandoffId(
        new[] { ExplorerHandoffService.InspectArgument, parsedId.ToString("N"), "extra" },
        out _),
    "Explorer handoff parser accepted trailing authority-bearing arguments.");
Require(
    !ExplorerHandoffService.TryParseHandoffId(
        new[] { ExplorerHandoffService.InspectArgument, Guid.Empty.ToString("N") },
        out _),
    "Explorer handoff parser accepted the empty GUID.");

string root = Path.Combine(Path.GetTempPath(), "SentinelExplorerHarness", Guid.NewGuid().ToString("N"));
string selectedRoot = Path.Combine(root, "Selected");
string handoffRoot = Path.Combine(root, "ExplorerHandoff");
Directory.CreateDirectory(selectedRoot);
string selectedFile = Path.Combine(selectedRoot, "résumé sample.txt");
File.WriteAllText(selectedFile, "safe test content");

try
{
    Require(
        ExplorerSelectionObjectValidator.TryReopenAndResolve(selectedFile, out string reopenedFile) &&
        Path.GetFullPath(selectedFile).Equals(reopenedFile, StringComparison.OrdinalIgnoreCase),
        "Sentinel could not reopen and resolve the selected file by Windows handle.");
    Require(
        ExplorerSelectionObjectValidator.TryReopenAndResolve(selectedRoot, out string reopenedDirectory) &&
        Path.GetFullPath(selectedRoot).Equals(reopenedDirectory, StringComparison.OrdinalIgnoreCase),
        "Sentinel could not reopen and resolve the selected directory by Windows handle.");
    Require(
        !ExplorerSelectionObjectValidator.TryReopenAndResolve(Path.Combine(selectedRoot, "missing.txt"), out _),
        "Exact-object validation accepted a missing filesystem object.");

    ExplorerHandoffService service = new(handoffRoot);
    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    Guid validId = Guid.NewGuid();
    WriteRecord(handoffRoot, validId, RecordJson("inspect", now, selectedFile, selectedFile));
    Require(service.TryConsume(validId, out ExplorerInspectionRequest? request, out string reason),
        "Valid Explorer handoff was rejected: " + reason);
    Require(request is not null && request.Paths.Count == 1,
        "Duplicate selected paths were not collapsed.");
    Require(Path.GetFullPath(selectedFile).Equals(request!.Paths[0], StringComparison.OrdinalIgnoreCase),
        "Selected file was not canonicalized to the expected path.");
    Require(!File.Exists(Path.Combine(handoffRoot, validId.ToString("N") + ".json")),
        "Consumed Explorer handoff record was not removed.");
    Require(!service.TryConsume(validId, out _, out _),
        "One-time Explorer handoff record could be consumed twice.");

    Guid staleId = Guid.NewGuid();
    WriteRecord(handoffRoot, staleId, RecordJson("inspect", DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds(), selectedFile));
    Require(!service.TryConsume(staleId, out _, out reason) && reason.Contains("stale", StringComparison.OrdinalIgnoreCase),
        "Stale Explorer handoff record was accepted.");

    Guid wrongCommandId = Guid.NewGuid();
    WriteRecord(handoffRoot, wrongCommandId, RecordJson("secure-delete", now, selectedFile));
    Require(!service.TryConsume(wrongCommandId, out _, out reason),
        "Explorer handoff accepted a destructive command.");

    Guid missingId = Guid.NewGuid();
    string missingPath = Path.Combine(selectedRoot, "missing.txt");
    WriteRecord(handoffRoot, missingId, RecordJson("inspect", now, missingPath));
    Require(!service.TryConsume(missingId, out _, out reason),
        "Explorer handoff accepted a filesystem item that Sentinel could not locate.");

    Guid extraFieldId = Guid.NewGuid();
    string extraField = $"{{\"Version\":1,\"Command\":\"inspect\",\"CreatedUnixMs\":{now},\"Items\":[{JsonSerializer.Serialize(selectedFile)}],\"BrokerToken\":\"forbidden\"}}";
    WriteRecord(handoffRoot, extraFieldId, extraField);
    Require(!service.TryConsume(extraFieldId, out _, out reason) && reason.Contains("malformed", StringComparison.OrdinalIgnoreCase),
        "Strict Explorer handoff parser accepted an unknown authority-bearing field.");

    Guid oversizedId = Guid.NewGuid();
    Directory.CreateDirectory(handoffRoot);
    File.WriteAllBytes(Path.Combine(handoffRoot, oversizedId.ToString("N") + ".json"), new byte[ExplorerHandoffService.MaximumRecordBytes + 1]);
    Require(!service.TryConsume(oversizedId, out _, out reason) && reason.Contains("oversized", StringComparison.OrdinalIgnoreCase),
        "Oversized Explorer handoff record was accepted.");

    Guid tooManyId = Guid.NewGuid();
    string[] tooMany = Enumerable.Repeat(selectedFile, ExplorerHandoffService.MaximumItems + 1).ToArray();
    WriteRecord(handoffRoot, tooManyId, RecordJson("inspect", now, tooMany));
    Require(!service.TryConsume(tooManyId, out _, out reason),
        "Explorer handoff accepted too many selected items.");

    Console.WriteLine("Exact activation arguments: PASS");
    Console.WriteLine("One-time bounded handoff: PASS");
    Console.WriteLine("Exact file/directory handle reopen: PASS");
    Console.WriteLine("Strict JSON / destructive-command rejection: PASS");
    Console.WriteLine("Stale/oversized/item-count rejection: PASS");
    Console.WriteLine("RESULT: PASS");
}
finally
{
    try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
}
