using System.Diagnostics;
using Sentinel.App.Services;

static ProcessStartInfo Cmd(string command)
{
    string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
    string executable = Path.Combine(system, "cmd.exe");
    ProcessStartInfo info = new()
    {
        FileName = executable,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    info.ArgumentList.Add("/d");
    info.ArgumentList.Add("/c");
    info.ArgumentList.Add(command);
    return info;
}

static ProcessStartInfo PowerShell(string script)
{
    string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
    string executable = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
    string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
    ProcessStartInfo info = new()
    {
        FileName = executable,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    info.ArgumentList.Add("-NoProfile");
    info.ArgumentList.Add("-NonInteractive");
    info.ArgumentList.Add("-EncodedCommand");
    info.ArgumentList.Add(encoded);
    return info;
}

static ProcessStartInfo PowerShellArguments(string script)
{
    string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
    string executable = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
    string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
    return new ProcessStartInfo
    {
        FileName = executable,
        Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
        UseShellExecute = false,
        CreateNoWindow = true
    };
}

static void Dump(string name, ProcessExecutionResult result, TimeSpan elapsed)
{
    string stdout = result.StandardOutput.Replace("\r", "\\r").Replace("\n", "\\n");
    string stderr = result.StandardError.Replace("\r", "\\r").Replace("\n", "\\n");
    if (stdout.Length > 200) stdout = stdout[..200] + "...";
    if (stderr.Length > 200) stderr = stderr[..200] + "...";
    Console.WriteLine($"{name}: ElapsedMs={elapsed.TotalMilliseconds:F0}; Outcome={result.Outcome}; ExitCode={result.ExitCode}; Truncated={result.OutputTruncated}; StdoutLen={result.StandardOutput.Length}; StderrLen={result.StandardError.Length}");
    Console.WriteLine($"{name} stdout: {stdout}");
    Console.WriteLine($"{name} stderr: {stderr}");
}

static async Task<ProcessExecutionResult> RunTimed(string name, ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken = default, int maxOutputChars = BoundedProcessRunner.DefaultMaxOutputChars)
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    ProcessExecutionResult result = await BoundedProcessRunner.RunAsync(startInfo, timeout, cancellationToken, maxOutputChars);
    stopwatch.Stop();
    Dump(name, result, stopwatch.Elapsed);
    return result;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Console.WriteLine("=== Sentinel AI BoundedProcessRunner Acceptance ===");
Console.WriteLine($"OS={Environment.OSVersion}; Framework={Environment.Version}; ProcessorCount={Environment.ProcessorCount}");

// Windows hosted runners can incur one-time PowerShell/AMSI initialization cost on the
// first powershell.exe process after image startup. Keep that environment warm-up separate
// from the security timeout assertions so a cold-runner cost is observable rather than
// misdiagnosed as a BoundedProcessRunner timeout defect. This does not change production
// behavior or relax any scenario timeout below.
Console.WriteLine("--- Diagnostic warm-up: first PowerShell process ---");
var warmup = await RunTimed("PowerShell warm-up", PowerShell("exit 0"), TimeSpan.FromSeconds(30));
Require(warmup.Outcome == ProcessExecutionOutcome.Succeeded, $"PowerShell warm-up failed: {warmup.Outcome}.");
Console.WriteLine("PowerShell warm-up: PASS");

Console.WriteLine("--- Scenario 1: cmd normal success ---");
var cmdSuccess = await RunTimed("cmd normal", Cmd("echo sentinel-ok"), TimeSpan.FromSeconds(10));
Require(cmdSuccess.Outcome == ProcessExecutionOutcome.Succeeded && cmdSuccess.StandardOutput.Contains("sentinel-ok", StringComparison.OrdinalIgnoreCase), "cmd normal success was not captured correctly.");
Console.WriteLine("cmd normal success: PASS");

Console.WriteLine("--- Scenario 2: PowerShell ArgumentList success ---");
var psSuccess = await RunTimed("PowerShell ArgumentList", PowerShell("Write-Output 'sentinel-ok'"), TimeSpan.FromSeconds(10));
Require(psSuccess.Outcome == ProcessExecutionOutcome.Succeeded && psSuccess.StandardOutput.Contains("sentinel-ok", StringComparison.Ordinal), "PowerShell ArgumentList success was not captured correctly.");
Console.WriteLine("PowerShell ArgumentList success: PASS");

Console.WriteLine("--- Scenario 3: production-style PowerShell Arguments success ---");
var psArgumentsSuccess = await RunTimed("PowerShell Arguments", PowerShellArguments("Write-Output 'sentinel-arguments-ok'"), TimeSpan.FromSeconds(20));
Require(psArgumentsSuccess.Outcome == ProcessExecutionOutcome.Succeeded && psArgumentsSuccess.StandardOutput.Contains("sentinel-arguments-ok", StringComparison.Ordinal), "Production-style PowerShell Arguments launch was not captured correctly.");
Console.WriteLine("PowerShell Arguments success: PASS");

Console.WriteLine("--- Scenario 4: nonzero exit ---");
var nonzero = await RunTimed("nonzero", PowerShell("Write-Error 'expected'; exit 7"), TimeSpan.FromSeconds(10));
Require(nonzero.Outcome == ProcessExecutionOutcome.NonZeroExit && nonzero.ExitCode == 7, "Nonzero exit was not surfaced correctly.");
Console.WriteLine("Nonzero exit: PASS");

Console.WriteLine("--- Scenario 5: simultaneous heavy stdout/stderr ---");
var heavy = await RunTimed(
    "heavy dual-stream",
    PowerShell("1..4000 | ForEach-Object { [Console]::Out.WriteLine(('O' * 128)); [Console]::Error.WriteLine(('E' * 128)) }"),
    TimeSpan.FromSeconds(20),
    maxOutputChars: 1_000_000);
Require(heavy.Outcome == ProcessExecutionOutcome.Succeeded, $"Heavy dual-stream output failed: {heavy.Outcome}.");
Require(heavy.StandardOutput.Length > 100_000 && heavy.StandardError.Length > 100_000, "Heavy dual-stream output was not drained concurrently.");
Console.WriteLine("Heavy dual-stream drain: PASS");

Console.WriteLine("--- Scenario 6: timeout ---");
var timedOut = await RunTimed("timeout", PowerShell("Start-Sleep -Seconds 30"), TimeSpan.FromMilliseconds(700));
Require(timedOut.Outcome == ProcessExecutionOutcome.TimedOut, $"Expected timeout, got {timedOut.Outcome}.");
Console.WriteLine("Wall-clock timeout: PASS");

Console.WriteLine("--- Scenario 7: cancellation ---");
using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700)))
{
    var canceled = await RunTimed("cancellation", PowerShell("Start-Sleep -Seconds 30"), TimeSpan.FromSeconds(30), cts.Token);
    Require(canceled.Outcome == ProcessExecutionOutcome.Canceled, $"Expected canceled, got {canceled.Outcome}.");
}
Console.WriteLine("Caller cancellation: PASS");

Console.WriteLine("--- Scenario 8: descendant tree termination ---");
string marker = Path.Combine(Path.GetTempPath(), "SentinelRunnerDescendant-" + Guid.NewGuid().ToString("N") + ".txt");
string escapedMarker = marker.Replace("'", "''", StringComparison.Ordinal);
string childScript = $"Start-Sleep -Seconds 3; Set-Content -LiteralPath '{escapedMarker}' -Value 'survived'";
string childEncoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(childScript));
string parentScript = $"Start-Process powershell.exe -ArgumentList '-NoProfile','-NonInteractive','-EncodedCommand','{childEncoded}'; Start-Sleep -Seconds 30";
var treeTimeout = await RunTimed("descendant timeout", PowerShell(parentScript), TimeSpan.FromMilliseconds(900));
Require(treeTimeout.Outcome == ProcessExecutionOutcome.TimedOut, $"Expected descendant scenario timeout, got {treeTimeout.Outcome}.");
await Task.Delay(TimeSpan.FromSeconds(4));
Require(!File.Exists(marker), "A descendant process survived timeout tree termination and wrote its marker file.");
Console.WriteLine("Descendant process-tree termination: PASS");
try { File.Delete(marker); } catch { }

Console.WriteLine("--- Scenario 9: bounded output ---");
var capped = await RunTimed(
    "capped",
    PowerShell("[Console]::Out.Write(('X' * 200000)); [Console]::Error.Write(('Y' * 200000))"),
    TimeSpan.FromSeconds(10),
    maxOutputChars: 4096);
Require(capped.Outcome == ProcessExecutionOutcome.Succeeded, $"Capped output process failed: {capped.Outcome}.");
Require(capped.OutputTruncated, "Large output was not reported as truncated.");
Require(capped.StandardOutput.Length <= 4096 && capped.StandardError.Length <= 4096, "Captured output exceeded configured cap.");
Console.WriteLine("Output cap: PASS");

Console.WriteLine("--- Scenario 10: launch failure ---");
var launchFailure = await RunTimed(
    "launch failure",
    new ProcessStartInfo
    {
        FileName = Path.Combine(Path.GetTempPath(), "sentinel-does-not-exist-" + Guid.NewGuid().ToString("N") + ".exe"),
        UseShellExecute = false,
        CreateNoWindow = true
    },
    TimeSpan.FromSeconds(5));
Require(launchFailure.Outcome == ProcessExecutionOutcome.LaunchFailure, $"Expected launch failure, got {launchFailure.Outcome}.");
Console.WriteLine("Launch failure: PASS");

Console.WriteLine("--- Scenario 11: process exit while output drains ---");
var exitWhileDraining = await RunTimed(
    "exit while draining",
    PowerShell("$s='Z' * 8192; 1..256 | ForEach-Object { [Console]::Out.Write($s); [Console]::Error.Write($s) }; exit 0"),
    TimeSpan.FromSeconds(20),
    maxOutputChars: 32768);
Require(exitWhileDraining.Outcome == ProcessExecutionOutcome.Succeeded, $"Exit-while-draining failed: {exitWhileDraining.Outcome}.");
Require(exitWhileDraining.OutputTruncated, "Exit-while-draining did not exercise bounded output truncation.");
Console.WriteLine("Exit while output drains: PASS");

Console.WriteLine("RESULT: PASS");
