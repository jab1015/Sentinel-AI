using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Trusted Knowledge Acceptance ===\n");

int failures = 0;
void Check(string name, bool pass)
{
    Console.WriteLine($"{name}: {(pass ? "PASS" : "FAIL")}");
    if (!pass) failures++;
}

string root = Path.Combine(Path.GetTempPath(), "SentinelAI-TrustedKnowledgeAcceptance", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string store = Path.Combine(root, "knowledge.json");
var engine = new TrustedKnowledgeEngine(store);
DateTimeOffset now = DateTimeOffset.UtcNow;

InvestigationInvalidationState state = new(
    DeviceInstanceId: "PCI\\VEN_8086",
    HardwareId: "VEN_8086",
    ErrorCode: "10",
    DriverVersion: "1.2.3",
    WindowsBuild: "26100",
    BiosVersion: "1.20",
    Manufacturer: "Dell",
    Model: "XPS",
    Severity: "Noncritical",
    VerifiedRepairSignature: "none");

var exhausted = new[]
{
    new RepairAttemptRecord("Windows Update", RepairAttemptOutcome.Unavailable, now, "No package"),
    new RepairAttemptRecord("OEM", RepairAttemptOutcome.Unavailable, now, "No package")
};

PersistentInvestigationRecord Record(
    InvestigationLifecycleState lifecycle,
    int confidence = 95,
    string trust = "Verified",
    InvestigationInvalidationState? evidence = null,
    IReadOnlyList<RepairAttemptRecord>? repairs = null) =>
    new(
        InvestigationId: Guid.NewGuid(),
        Fingerprint: "fp-1",
        FindingType: "Driver",
        RootCause: "Persistent device condition",
        EvidenceSummary: "Verified evidence",
        ConfidencePercent: confidence,
        TrustLevel: trust,
        RiskClassification: lifecycle == InvestigationLifecycleState.Critical ? "Critical" : "Noncritical",
        State: lifecycle,
        RepairAttempts: repairs ?? exhausted,
        FirstDetectedUtc: now.AddDays(-1),
        LastVerifiedUtc: now,
        InvalidationState: evidence ?? state,
        NotificationsSuppressed: false,
        SuppressedAtUtc: null,
        SuppressionReason: string.Empty);

Console.WriteLine("--- Scenario 1: verified exhausted noncritical investigation can become trusted knowledge ---");
var promoted = await engine.PromoteAsync(Record(InvestigationLifecycleState.PersistentNoncritical));
Check("Promotion accepted", promoted.Promoted);
Check("Knowledge created", promoted.Record is not null);

Console.WriteLine("\n--- Scenario 2: incomplete investigation cannot become trusted knowledge ---");
var incomplete = await engine.PromoteAsync(Record(InvestigationLifecycleState.InvestigationIncomplete));
Check("Incomplete promotion rejected", !incomplete.Promoted);

Console.WriteLine("\n--- Scenario 3: critical investigation cannot become trusted reusable knowledge ---");
var critical = await engine.PromoteAsync(Record(InvestigationLifecycleState.Critical));
Check("Critical promotion rejected", !critical.Promoted);

Console.WriteLine("\n--- Scenario 4: low-confidence conclusion cannot be promoted ---");
var low = await engine.PromoteAsync(Record(InvestigationLifecycleState.PersistentNoncritical, confidence: 60));
Check("Low confidence rejected", !low.Promoted);

Console.WriteLine("\n--- Scenario 5: unchanged evidence reuses trusted conclusion ---");
var reuse = await engine.FindReusableAsync("Driver", state, currentConditionCritical: false);
Check("Trusted conclusion reusable", reuse.Reused);
Check("Stored knowledge returned", reuse.Record is not null);

Console.WriteLine("\n--- Scenario 6: material evidence change invalidates trusted conclusion ---");
var changed = state with { DriverVersion = "2.0.0" };
var invalid = await engine.FindReusableAsync("Driver", changed, currentConditionCritical: false);
Check("Changed evidence rejected", !invalid.Reused);
Check("Material change recognized", PersistentInvestigationMemoryService.HasMaterialChange(state, changed));

Console.WriteLine("\n--- Scenario 7: expired knowledge requires revalidation ---");
var knowledge = promoted.Record!;
var expiredCopy = knowledge with { ExpiresUtc = now.AddMinutes(-1) };
Check("Expired knowledge recognized", expiredCopy.IsExpired(now));
Check("Valid knowledge not expired", !knowledge.IsExpired(now));

Console.WriteLine("\n--- Scenario 8: current critical evidence always forces fresh investigation ---");
var criticalReuse = await engine.FindReusableAsync("Driver", state, currentConditionCritical: true);
Check("Critical current evidence not reused", !criticalReuse.Reused);
Check("Critical evidence rejection explains direct investigation", criticalReuse.Message.Contains("critical", StringComparison.OrdinalIgnoreCase));

try { Directory.Delete(root, recursive: true); } catch { }

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : $"FAIL ({failures})")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
