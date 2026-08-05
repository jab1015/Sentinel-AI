using Sentinel.App.Services;
using Kind = Sentinel.App.Services.FriendlyValueSummaryService.ValueActionKind;
using ValueAction = Sentinel.App.Services.FriendlyValueSummaryService.VerifiedValueAction;

Console.WriteLine("=== Sentinel AI Friendly Value Summary Acceptance ===\n");

int failures = 0;
void Check(string name, bool condition)
{
    Console.WriteLine($"{name}: {(condition ? "PASS" : "FAIL")}");
    if (!condition) failures++;
}

var service = new FriendlyValueSummaryService();

Console.WriteLine("--- Scenario 1: unverified work is never claimed ---");
var unverified = service.CreateSummary(new[]
{
    new ValueAction(Kind.DriveOptimization, Completed: true, Verified: false)
});
Check("Unverified action suppressed", unverified is null);

Console.WriteLine("\n--- Scenario 2: incomplete work is never claimed ---");
var incomplete = service.CreateSummary(new[]
{
    new ValueAction(Kind.DiskCheck, Completed: false, Verified: true)
});
Check("Incomplete action suppressed", incomplete is null);

Console.WriteLine("\n--- Scenario 3: one verified maintenance action gets friendly language ---");
var one = service.CreateSummary(new[]
{
    new ValueAction(Kind.DriveOptimization, Completed: true, Verified: true)
});
Check("Single summary created", one is not null);
Check("Single summary friendly title", one?.Title == "A little housekeeping is done.");
Check("Drive optimization described", one?.Message.Contains("optimized your drive", StringComparison.OrdinalIgnoreCase) == true);
Check("Verification outcome stated", one?.Message.Contains("checked out successfully", StringComparison.OrdinalIgnoreCase) == true);

Console.WriteLine("\n--- Scenario 4: multiple verified actions become one readable tune-up summary ---");
var multiple = service.CreateSummary(new[]
{
    new ValueAction(Kind.DriveOptimization, true, true),
    new ValueAction(Kind.DiskCheck, true, true),
    new ValueAction(Kind.TemporaryFileCleanup, true, true)
});
Check("Multiple summary created", multiple is not null);
Check("Tune-up title used", multiple?.Title == "I gave your computer a quick tune-up.");
Check("Drive optimization included", multiple?.Message.Contains("optimized your drive", StringComparison.OrdinalIgnoreCase) == true);
Check("Disk check included", multiple?.Message.Contains("checked your drive for problems", StringComparison.OrdinalIgnoreCase) == true);
Check("Cleanup included", multiple?.Message.Contains("temporary files", StringComparison.OrdinalIgnoreCase) == true);

Console.WriteLine("\n--- Scenario 5: verified repair uses stronger value language without exaggeration ---");
var repair = service.CreateSummary(new[]
{
    new ValueAction(Kind.SystemFileRepair, true, true, ProblemFoundAndResolved: true)
});
Check("Repair summary created", repair is not null);
Check("Repair title used", repair?.Title == "I found something and took care of it.");
Check("Repair explicitly described", repair?.Message.Contains("repaired Windows system files", StringComparison.OrdinalIgnoreCase) == true);
Check("Post-work verification mentioned", repair?.Message.Contains("checked again", StringComparison.OrdinalIgnoreCase) == true);

Console.WriteLine("\n--- Scenario 6: mixed verified and unverified actions only report verified work ---");
var mixed = service.CreateSummary(new[]
{
    new ValueAction(Kind.NetworkRepair, true, true),
    new ValueAction(Kind.DriverRepair, true, false),
    new ValueAction(Kind.StartupOptimization, false, true)
});
Check("Mixed summary created", mixed is not null);
Check("Verified network work included", mixed?.Message.Contains("network settings", StringComparison.OrdinalIgnoreCase) == true);
Check("Unverified driver work excluded", mixed?.Message.Contains("driver repair", StringComparison.OrdinalIgnoreCase) != true);
Check("Incomplete startup work excluded", mixed?.Message.Contains("startup", StringComparison.OrdinalIgnoreCase) != true);

Console.WriteLine("\n--- Scenario 7: duplicate action types do not spam the user ---");
var duplicate = service.CreateSummary(new[]
{
    new ValueAction(Kind.DiskCheck, true, true),
    new ValueAction(Kind.DiskCheck, true, true)
});
Check("Duplicate action summary created", duplicate is not null);
Check("Duplicate action collapsed", duplicate?.VerifiedActions.Count == 1);

Console.WriteLine("\n--- Scenario 8: friendly summaries remain nontechnical ---");
var friendly = service.CreateSummary(new[]
{
    new ValueAction(Kind.SecurityRepair, true, true, true),
    new ValueAction(Kind.TemporaryFileCleanup, true, true)
});
string friendlyText = $"{friendly?.Title} {friendly?.Message}";
Check("No command-line jargon", !friendlyText.Contains("cmd", StringComparison.OrdinalIgnoreCase));
Check("No registry jargon", !friendlyText.Contains("registry", StringComparison.OrdinalIgnoreCase));
Check("No implementation jargon", !friendlyText.Contains("exit code", StringComparison.OrdinalIgnoreCase));

Console.WriteLine($"\nRESULT: {(failures == 0 ? "PASS" : "FAIL")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
