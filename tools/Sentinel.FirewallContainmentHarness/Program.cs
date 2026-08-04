using Sentinel.App.Services;

const string testIp = "203.0.113.10"; // TEST-NET-3, documentation-only address

Console.WriteLine("=== Sentinel AI Firewall Containment Acceptance ===");

FirewallContainmentService service = new();

Console.WriteLine();
Console.WriteLine("--- Scenario 1: verified narrow outbound block ---");
Console.WriteLine($"Target: {testIp}");

FirewallContainmentService.FirewallContainmentResult block = await service.BlockEndpointAsync(testIp);
Console.WriteLine($"Block attempted: {block.Attempted}");
Console.WriteLine($"Block succeeded: {block.Succeeded}");
Console.WriteLine($"Block title: {block.Title}");
Console.WriteLine($"Rolled back automatically: {block.RolledBack}");
Console.WriteLine($"Connectivity healthy: {block.ConnectivityHealthy}");

bool blockPass = block.Attempted &&
                 block.Succeeded &&
                 !block.RolledBack &&
                 block.ConnectivityHealthy;
Console.WriteLine($"Block scenario: {(blockPass ? "PASS" : "FAIL")}");

Console.WriteLine();
Console.WriteLine("--- Scenario 2: verified reversal/removal ---");

FirewallContainmentService.FirewallContainmentResult remove = await service.RemoveBlockAsync(testIp);
Console.WriteLine($"Removal attempted: {remove.Attempted}");
Console.WriteLine($"Removal succeeded: {remove.Succeeded}");
Console.WriteLine($"Removal title: {remove.Title}");
Console.WriteLine($"Connectivity healthy after removal: {remove.ConnectivityHealthy}");

bool reversalPass = remove.Attempted &&
                    remove.Succeeded &&
                    remove.RolledBack &&
                    remove.ConnectivityHealthy;
Console.WriteLine($"Reversal scenario: {(reversalPass ? "PASS" : "FAIL")}");

bool pass = blockPass && reversalPass;

Console.WriteLine();
Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
Environment.ExitCode = pass ? 0 : 1;
