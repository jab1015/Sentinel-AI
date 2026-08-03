using Sentinel.App.Services;
using System.Net.Sockets;

const string rollbackTargetIp = "23.197.222.64";
const int targetPort = 443;
const string persistentTestIp = "203.0.113.10"; // TEST-NET-3, documentation-only address

Console.WriteLine("=== Sentinel AI Firewall Containment Acceptance ===");

static async Task<bool> CanConnectAsync(string host, int port)
{
    try
    {
        using TcpClient client = new();
        await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(5));
        return client.Connected;
    }
    catch
    {
        return false;
    }
}

FirewallContainmentService service = new();

Console.WriteLine();
Console.WriteLine("--- Scenario 1: automatic rollback when connectivity is affected ---");
Console.WriteLine($"Target: {rollbackTargetIp}:{targetPort}");
bool rollbackBaseline = await CanConnectAsync(rollbackTargetIp, targetPort);
Console.WriteLine($"Baseline endpoint connectivity: {rollbackBaseline}");

FirewallContainmentService.FirewallContainmentResult rollbackBlock = await service.BlockEndpointAsync(rollbackTargetIp);
Console.WriteLine($"Block attempted: {rollbackBlock.Attempted}");
Console.WriteLine($"Block succeeded: {rollbackBlock.Succeeded}");
Console.WriteLine($"Block title: {rollbackBlock.Title}");
Console.WriteLine($"Rolled back automatically: {rollbackBlock.RolledBack}");

await Task.Delay(1000);
bool rollbackConnectivityRestored = await CanConnectAsync(rollbackTargetIp, targetPort);
Console.WriteLine($"Endpoint connectivity restored after rollback: {rollbackConnectivityRestored}");

bool rollbackPass = rollbackBaseline &&
                    rollbackBlock.Attempted &&
                    !rollbackBlock.Succeeded &&
                    rollbackBlock.RolledBack &&
                    rollbackConnectivityRestored;
Console.WriteLine($"Rollback scenario: {(rollbackPass ? "PASS" : "FAIL")}");

Console.WriteLine();
Console.WriteLine("--- Scenario 2: narrow block persists when internet remains healthy ---");
Console.WriteLine($"Target: {persistentTestIp}");

FirewallContainmentService.FirewallContainmentResult persistentBlock = await service.BlockEndpointAsync(persistentTestIp);
Console.WriteLine($"Block attempted: {persistentBlock.Attempted}");
Console.WriteLine($"Block succeeded: {persistentBlock.Succeeded}");
Console.WriteLine($"Block title: {persistentBlock.Title}");
Console.WriteLine($"Rolled back automatically: {persistentBlock.RolledBack}");
Console.WriteLine($"Connectivity healthy: {persistentBlock.ConnectivityHealthy}");

bool persistentCreated = persistentBlock.Succeeded &&
                         !persistentBlock.RolledBack &&
                         persistentBlock.ConnectivityHealthy;

FirewallContainmentService.FirewallContainmentResult remove = await service.RemoveBlockAsync(persistentTestIp);
Console.WriteLine($"Removal succeeded: {remove.Succeeded}");
Console.WriteLine($"Removal title: {remove.Title}");
Console.WriteLine($"Connectivity healthy after removal: {remove.ConnectivityHealthy}");

bool persistentPass = persistentCreated && remove.Succeeded;
Console.WriteLine($"Persistent narrow-block scenario: {(persistentPass ? "PASS" : "FAIL")}");

bool pass = rollbackPass && persistentPass;

Console.WriteLine();
Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
Environment.ExitCode = pass ? 0 : 1;
