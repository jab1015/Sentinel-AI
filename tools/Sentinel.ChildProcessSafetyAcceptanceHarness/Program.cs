using Sentinel.App.Services;
using System.Diagnostics;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
string fullExpandPath = string.IsNullOrWhiteSpace(systemDirectory)
    ? "expand.exe"
    : Path.Combine(systemDirectory, "expand.exe");

bool preflightBlocked = ChildProcessSafetyPolicy.IsBlocked(
    new ChildProcessSafetyPolicy.ProcessStartInfoLike(fullExpandPath),
    out string preflightReason);
Assert(preflightBlocked,
    "Full-path expand.exe must be rejected by the preflight policy before resource acquisition.");
Assert(preflightReason.Contains("disk consumption", StringComparison.OrdinalIgnoreCase),
    "Preflight rejection must explain the filesystem quota safety reason.");

ProcessStartInfo blocked = new()
{
    FileName = "expand.exe",
    UseShellExecute = false,
    CreateNoWindow = true
};
blocked.ArgumentList.Add("/?");

ProcessExecutionResult blockedResult = await BoundedProcessRunner.RunAsync(
    blocked,
    TimeSpan.FromSeconds(5),
    maxOutputChars: 4096);

Assert(blockedResult.Outcome == ProcessExecutionOutcome.LaunchFailure,
    "expand.exe must be rejected before launch.");
Assert(blockedResult.Detail.Contains("disk consumption", StringComparison.OrdinalIgnoreCase),
    "Blocked expansion must explain the filesystem quota safety reason.");

ProcessStartInfo allowed = new()
{
    FileName = "cmd.exe",
    UseShellExecute = false,
    CreateNoWindow = true
};
allowed.ArgumentList.Add("/d");
allowed.ArgumentList.Add("/c");
allowed.ArgumentList.Add("exit 0");

ProcessExecutionResult allowedResult = await BoundedProcessRunner.RunAsync(
    allowed,
    TimeSpan.FromSeconds(5),
    maxOutputChars: 4096);

Assert(allowedResult.Succeeded, "Normal bounded child processes must remain allowed.");

Console.WriteLine("Child process safety acceptance harness PASS");
