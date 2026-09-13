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
        new[] { ExplorerHandoffService.HandoffArgument, parsedId.ToString("N") },
        out Guid parsed) && parsed == parsedId,
    "Exact Explorer handoff arguments were not accepted.");
Require(
    ExplorerHandoffService.TryParseHandoffId(
        $"{ExplorerHandoffService.HandoffArgument} {parsedId:N}",
        out parsed) && parsed == parsedId,
    "Launch argument string was not parsed.");
Require(
    !ExplorerHandoffService.TryParseHandoffId(
        new[] { ExplorerHandoffService.HandoffArgument, parsedId.ToString("N"), "extra" },
        out _),
    "Explorer handoff parser accepted trailing authority-bearing arguments.");
Require(
    !ExplorerHandoffService.TryParseHandoffId(
        new[] { ExplorerHandoffService.HandoffArgument, Guid.Empty.ToString("N") },
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
    WriteRecord(handoffRoot, validId, RecordJson(ExplorerHandoffService.InspectCommand, now, selectedFile, selectedFile));
    Require(service.TryConsume(validId, out ExplorerInspectionRequest? request, out string reason),
        "Valid Explorer inspection handoff was rejected: " + reason);
    Require(request is not null && request.Action == ExplorerRequestedAction.Inspect && request.Paths.Count == 1,
        "Inspection action or duplicate-collapse semantics were incorrect.");
    Require(Path.GetFullPath(selectedFile).Equals(request!.Paths[0], StringComparison.OrdinalIgnoreCase),
        "Selected file was not canonicalized to the expected path.");
    Require(!File.Exists(Path.Combine(handoffRoot, validId.ToString("N") + ".json")),
        "Consumed Explorer handoff record was not removed.");
    Require(!service.TryConsume(validId, out _, out _),
        "One-time Explorer handoff record could be consumed twice.");

    (string Command, ExplorerRequestedAction Action)[] premiumCommands =
    {
        (ExplorerHandoffService.EncryptCommand, ExplorerRequestedAction.EncryptFile),
        (ExplorerHandoffService.VaultCommand, ExplorerRequestedAction.AddToVault),
        (ExplorerHandoffService.SecureDeleteCommand, ExplorerRequestedAction.SecureDelete)
    };
    foreach ((string command, ExplorerRequestedAction action) in premiumCommands)
    {
        Guid premiumId = Guid.NewGuid();
        WriteRecord(handoffRoot, premiumId, RecordJson(command, now, selectedFile));
        Require(service.TryConsume(premiumId, out ExplorerInspectionRequest? premiumRequest, out reason),
            $"Supported privacy intent '{command}' was rejected: {reason}");
        Require(premiumRequest is not null && premiumRequest.Action == action && premiumRequest.Paths.Count == 1,
            $"Privacy intent '{command}' did not map to the expected action.");
    }

    Guid multiPrivacyId = Guid.NewGuid();
    string secondFile = Path.Combine(selectedRoot, "second.txt");
    File.WriteAllText(secondFile, "second");
    WriteRecord(handoffRoot, multiPrivacyId, RecordJson(ExplorerHandoffService.SecureDeleteCommand, now, selectedFile, secondFile));
    Require(!service.TryConsume(multiPrivacyId, out _, out reason) && reason.Contains("exactly one", StringComparison.OrdinalIgnoreCase),
        "Destructive Explorer intent accepted a multi-file selection.");

    Guid directoryPrivacyId = Guid.NewGuid();
    WriteRecord(handoffRoot, directoryPrivacyId, RecordJson(ExplorerHandoffService.SecureDeleteCommand, now, selectedRoot));
    Require(!service.TryConsume(directoryPrivacyId, out _, out reason) && reason.Contains("files only", StringComparison.OrdinalIgnoreCase),
        "Destructive Explorer intent accepted a directory.");

    Guid unknownCommandId = Guid.NewGuid();
    WriteRecord(handoffRoot, unknownCommandId, RecordJson("delete-path", now, selectedFile));
    Require(!service.TryConsume(unknownCommandId, out _, out reason),
        "Explorer handoff accepted an unsupported generic destructive command.");

    Guid staleId = Guid.NewGuid();
    WriteRecord(handoffRoot, staleId, RecordJson(ExplorerHandoffService.InspectCommand, DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds(), selectedFile));
    Require(!service.TryConsume(staleId, out _, out reason) && reason.Contains("stale", StringComparison.OrdinalIgnoreCase),
        "Stale Explorer handoff record was accepted.");

    Guid missingId = Guid.NewGuid();
    string missingPath = Path.Combine(selectedRoot, "missing.txt");
    WriteRecord(handoffRoot, missingId, RecordJson(ExplorerHandoffService.InspectCommand, now, missingPath));
    Require(!service.TryConsume(missingId, out _, out reason),
        "Explorer handoff accepted a filesystem item that Sentinel could not locate.");

    Guid extraFieldId = Guid.NewGuid();
    string extraField = $"{{\"Version\":1,\"Command\":\"inspect\",\"CreatedUnixMs\":{now},\"Items\":[{JsonSerializer.Serialize(selectedFile)}],\"BrokerToken\":\"forbidden\"}}";
    WriteRecord(handoffRoot, extraFieldId, extraField);
    Require(!service.TryConsume(extraFieldId, out _, out reason) && reason.Contains("malformed", StringComparison.OrdinalIgnoreCase),
        "Strict Explorer handoff parser accepted an unknown authority-bearing field.");

    Guid oversizedId = Guid.NewGuid();
    File.WriteAllBytes(Path.Combine(handoffRoot, oversizedId.ToString("N") + ".json"), new byte[ExplorerHandoffService.MaximumRecordBytes + 1]);
    Require(!service.TryConsume(oversizedId, out _, out reason) && reason.Contains("oversized", StringComparison.OrdinalIgnoreCase),
        "Oversized Explorer handoff record was accepted.");

    Guid tooManyId = Guid.NewGuid();
    string[] tooMany = Enumerable.Repeat(selectedFile, ExplorerHandoffService.MaximumItems + 1).ToArray();
    WriteRecord(handoffRoot, tooManyId, RecordJson(ExplorerHandoffService.InspectCommand, now, tooMany));
    Require(!service.TryConsume(tooManyId, out _, out reason),
        "Explorer handoff accepted too many selected items.");

    Console.WriteLine("Exact activation arguments: PASS");
    Console.WriteLine("One-time bounded handoff: PASS");
    Console.WriteLine("Exact file/directory handle reopen: PASS");
    Console.WriteLine("Privacy intent mapping without authority fields: PASS");
    Console.WriteLine("Destructive multi-file/directory/generic-command rejection: PASS");
    Console.WriteLine("Strict JSON / stale / oversized / item-count rejection: PASS");
    Console.WriteLine("RESULT: PASS");
}
finally
{
    try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
}
