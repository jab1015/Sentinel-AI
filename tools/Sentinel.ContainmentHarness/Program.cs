using Sentinel.App.Services;
using System.Diagnostics;

Console.WriteLine("=== Sentinel AI Process Containment Acceptance ===");

using Process notepad = Process.Start(new ProcessStartInfo
{
    FileName = "notepad.exe",
    UseShellExecute = true
}) ?? throw new InvalidOperationException("Could not start Notepad test process.");

await Task.Delay(750);

int pid = notepad.Id;
Console.WriteLine($"Disposable test process started: notepad PID {pid}");

ProcessContainmentService service = new();
ProcessContainmentService.ProcessContainmentResult result = await service.ContainAsync("notepad");

Console.WriteLine($"Attempted: {result.Attempted}");
Console.WriteLine($"Succeeded: {result.Succeeded}");
Console.WriteLine($"Title: {result.Title}");
Console.WriteLine($"Summary: {result.Summary}");

bool stillRunning;
try
{
    using Process check = Process.GetProcessById(pid);
    stillRunning = !check.HasExited;
}
catch (ArgumentException)
{
    stillRunning = false;
}

Console.WriteLine($"Exact PID still running: {stillRunning}");

ProcessContainmentService.ProcessContainmentResult protectedResult = await service.ContainAsync("explorer");
Console.WriteLine($"Protected-process refusal: {!protectedResult.Succeeded && protectedResult.Title.Contains("blocked", StringComparison.OrdinalIgnoreCase)}");
Console.WriteLine($"Protected-process summary: {protectedResult.Summary}");

bool pass = result.Succeeded && !stillRunning &&
            !protectedResult.Succeeded &&
            protectedResult.Title.Contains("blocked", StringComparison.OrdinalIgnoreCase);

Console.WriteLine();
Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
Environment.ExitCode = pass ? 0 : 1;
