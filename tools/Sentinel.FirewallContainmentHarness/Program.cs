using Sentinel.App.Services;
using System.Net.Sockets;

const string targetIp = "23.197.222.64";
const int targetPort = 443;

Console.WriteLine("=== Sentinel AI Firewall Containment Acceptance ===");
Console.WriteLine($"Target: {targetIp}:{targetPort}");

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

bool baseline = await CanConnectAsync(targetIp, targetPort);
Console.WriteLine($"Baseline endpoint connectivity: {baseline}");

FirewallContainmentService service = new();
FirewallContainmentService.FirewallContainmentResult block = await service.BlockEndpointAsync(targetIp);
Console.WriteLine($"Block attempted: {block.Attempted}");
Console.WriteLine($"Block succeeded: {block.Succeeded}");
Console.WriteLine($"Block title: {block.Title}");
Console.WriteLine($"Block summary: {block.Summary}");
Console.WriteLine($"Rolled back automatically: {block.RolledBack}");

await Task.Delay(1000);
bool blockedEndpointStillReachable = await CanConnectAsync(targetIp, targetPort);
Console.WriteLine($"Blocked endpoint still reachable: {blockedEndpointStillReachable}");

FirewallContainmentService.FirewallContainmentResult remove = await service.RemoveBlockAsync(targetIp);
Console.WriteLine($"Removal succeeded: {remove.Succeeded}");
Console.WriteLine($"Removal title: {remove.Title}");
Console.WriteLine($"Removal summary: {remove.Summary}");

await Task.Delay(1000);
bool restored = await CanConnectAsync(targetIp, targetPort);
Console.WriteLine($"Endpoint connectivity restored: {restored}");

bool pass = baseline &&
            block.Succeeded &&
            !block.RolledBack &&
            !blockedEndpointStillReachable &&
            remove.Succeeded &&
            restored;

Console.WriteLine();
Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
Environment.ExitCode = pass ? 0 : 1;
