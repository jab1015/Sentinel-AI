using Sentinel.App.Models;
using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Trusted Knowledge Acceptance ===\n");
var failures = 0;
void Check(string name, bool pass) { Console.WriteLine($"{name}: {(pass ? "PASS" : "FAIL")}"); if (!pass) failures++; }

var now = DateTimeOffset.UtcNow;
InvestigationInvalidationState state = new("PCI\\VEN_8086", "VEN_8086", "10", "1.2.3", "26100", "1.20", "Dell", "XPS", "Noncritical", "none");
var exhausted = new[] { new RepairAttemptRecord("Windows Update", RepairAttemptOutcome.Unavailable, now, "No package"), new RepairAttemptRecord("OEM", RepairAttemptOutcome.Unavailable, now, "No package") };
PersistentInvestigationRecord Record(InvestigationLifecycleState lifecycle, int confidence = 95, string trust = "Verified", InvestigationInvalidationState? s = null, IReadOnlyList<RepairAttemptRecord>? repairs = null) =>
    new(Guid.NewGuid(), "fp-1", "Driver", "Persistent device condition", "Verified evidence", confidence, trust, "Noncritical", lifecycle, repairs ?? exhausted, now.AddDays(-1), now, s ?? state, false, null, "");

Console.WriteLine("--- Scenario 1: verified exhausted noncritical investigation can become trusted knowledge ---");
var engine = new TrustedKnowledgeEngine();
var promoted = engine.TryPromote(Record(InvestigationLifecycleState.PersistentNoncritical), now);
Check("Promotion accepted", promoted.Accepted);
Check("Knowledge created", promoted.Knowledge is not null);

Console.WriteLine("\n--- Scenario 2: incomplete investigation cannot become trusted knowledge ---");
var incomplete = engine.TryPromote(Record(InvestigationLifecycleState.InvestigationIncomplete), now);
Check("Incomplete promotion rejected", !incomplete.Accepted);

Console.WriteLine("\n--- Scenario 3: critical investigation cannot be reused as trusted knowledge ---");
var critical = engine.TryPromote(Record(InvestigationLifecycleState.Critical), now);
Check("Critical promotion rejected", !critical.Accepted);

Console.WriteLine("\n--- Scenario 4: low-confidence conclusion cannot be promoted ---");
var low = engine.TryPromote(Record(InvestigationLifecycleState.PersistentNoncritical, 60), now);
Check("Low confidence rejected", !low.Accepted);

Console.WriteLine("\n--- Scenario 5: unchanged evidence reuses trusted conclusion ---");
var knowledge = promoted.Knowledge!;
var reuse = engine.EvaluateReuse(knowledge, state, now.AddHours(1));
Check("Trusted conclusion reusable", reuse.Reusable);
Check("Fresh investigation not required", !reuse.RequiresFreshInvestigation);

Console.WriteLine("\n--- Scenario 6: material evidence change invalidates trusted conclusion ---");
var changed = state with { DriverVersion = "2.0.0" };
var invalid = engine.EvaluateReuse(knowledge, changed, now.AddHours(1));
Check("Changed evidence rejected", !invalid.Reusable);
Check("Fresh investigation required", invalid.RequiresFreshInvestigation);

Console.WriteLine("\n--- Scenario 7: expired knowledge requires revalidation ---");
var expired = engine.EvaluateReuse(knowledge, state, knowledge.RevalidateAfterUtc.AddMinutes(1));
Check("Expired knowledge not reused", !expired.Reusable);
Check("Revalidation required", expired.RequiresFreshInvestigation);

Console.WriteLine("\n--- Scenario 8: current critical evidence always forces fresh investigation ---");
var criticalState = state with { Severity = "Critical" };
var criticalReuse = engine.EvaluateReuse(knowledge, criticalState, now.AddHours(1));
Check("Critical current evidence not reused", !criticalReuse.Reusable);
Check("Critical evidence forces investigation", criticalReuse.RequiresFreshInvestigation);

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : "FAIL")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
