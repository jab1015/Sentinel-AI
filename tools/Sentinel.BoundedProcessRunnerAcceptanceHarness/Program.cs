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

static void Dump(string name, ProcessExecutionResult result)
{
    string stdout = result.StandardOutput.Replace("\r", "\\r").Replace("\n", "\\n");
    string stderr = result.StandardError.Replace("\r", "\\r").Replace("\n", "\\n");
    if (stdout.Length > 200) stdout = stdout[..200] + "...";
    if (stderr.Length > 200) stderr = stderr[..200] + "...";
    Console.WriteLine($"{name}: Outcome={result.Outcome}; ExitCode={result.ExitCode}; Truncated={result.OutputTruncated}; StdoutLen={result.StandardOutput.Length}; StderrLen={result.StandardError.Length}");
    Console.WriteLine($"{name} stdout: {stdout}");
    Console.WriteLine($"{name} stderr: {stderr}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Console.WriteLine("=== Sentinel AI BoundedProcessRunner Acceptance ===");

Console.WriteLine("--- Scenario 1: cmd normal success ---");
var cmdSuccess = await BoundedProcessRunner.RunAsync(Cmd("echo sentinel-ok"), TimeSpan.FromSeconds(10));
Dump("cmd normal", cmdSuccess);
Require(cmdSuccess.Outcome == ProcessExecutionOutcome.Succeeded && cmdSuccess.StandardOutput.Contains("sentinel-ok", StringComparison.OrdinalIgnoreCase), "cmd normal success was not captured correctly.");
Console.WriteLine("cmd normal success: PASS");

Console.WriteLine("--- Scenario 2: PowerShell normal success ---");
var psSuccess = await BoundedProcessRunner.RunAsync(PowerShell("Write-Output 'sentinel-ok'"), TimeSpan.FromSeconds(10));
Dump("PowerShell normal", psSuccess);
Require(psSuccess.Outcome == ProcessExecutionOutcome.Succeeded && psSuccess.StandardOutput.Contains("sentinel-ok", StringComparison.Ordinal), "PowerShell normal success was not captured correctly.");
Console.WriteLine("PowerShell normal success: PASS");

Console.WriteLine("--- Scenario 3: nonzero exit ---");
var nonzero = await BoundedProcessRunner.RunAsync(PowerShell("Write-Error 'expected'; exit 7"), TimeSpan.FromSeconds(10));
Dump("nonzero", nonzero);
Require(nonzero.Outcome == ProcessExecutionOutcome.NonZeroExit && nonzero.ExitCode == 7, "Nonzero exit was not surfaced correctly.");
Console.WriteLine("Nonzero exit: PASS");

Console.WriteLine("--- Scenario 4: simultaneous heavy stdout/stderr ---");
var heavy = await BoundedProcessRunner.RunAsync(
    PowerShell("1..4000 | ForEach-Object { [Console]::Out.WriteLine(('O' * 128)); [Console]::Error.WriteLine(('E' * 128)) }"),
    TimeSpan.FromSeconds(20),
    maxOutputChars: 1_000_000);
Dump("heavy dual-stream", heavy);
Require(heavy.Outcome == ProcessExecutionOutcome.Succeeded, $"Heavy dual-stream output failed: {heavy.Outcome}.");
Require(heavy.StandardOutput.Length > 100_000 && heavy.StandardError.Length > 100_000, "Heavy dual-stream output was not drained concurrently.");
Console.WriteLine("Heavy dual-stream drain: PASS");

Console.WriteLine("--- Scenario 5: timeout ---");
var timedOut = await BoundedProcessRunner.RunAsync(PowerShell("Start-Sleep -Seconds 30"), TimeSpan.FromMilliseconds(700));
Dump("timeout", timedOut);
Require(timedOut.Outcome == ProcessExecutionOutcome.TimedOut, $"Expected timeout, got {timedOut.Outcome}.");
Console.WriteLine("Wall-clock timeout: PASS");

Console.WriteLine("--- Scenario 6: cancellation ---");
using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700)))
{
    var canceled = await BoundedProcessRunner.RunAsync(PowerShell("Start-Sleep -Seconds 30"), TimeSpan.FromSeconds(30), cts.Token);
    Dump("cancellation", canceled);
    Require(canceled.Outcome == ProcessExecutionOutcome.Canceled, $"Expected canceled, got {canceled.Outcome}.");
}
Console.WriteLine("Caller cancellation: PASS");

Console.WriteLine("--- Scenario 7: bounded output ---");
var capped = await BoundedProcessRunner.RunAsync(
    PowerShell("[Console]::Out.Write(('X' * 200000)); [Console]::Error.Write(('Y' * 200000))"),
    TimeSpan.FromSeconds(10),
    maxOutputChars: 4096);
Dump("capped", capped);
Require(capped.Outcome == ProcessExecutionOutcome.Succeeded, $"Capped output process failed: {capped.Outcome}.");
Require(capped.OutputTruncated, "Large output was not reported as truncated.");
Require(capped.StandardOutput.Length <= 4096 && capped.StandardError.Length <= 4096, "Captured output exceeded configured cap.");
Console.WriteLine("Output cap: PASS");

Console.WriteLine("RESULT: PASS");
