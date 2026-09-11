using System.Diagnostics;
using Sentinel.App.Services;

static ProcessStartInfo PowerShell(string script)
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

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Console.WriteLine("=== Sentinel AI BoundedProcessRunner Acceptance ===");

Console.WriteLine("--- Scenario 1: normal success ---");
var success = await BoundedProcessRunner.RunAsync(PowerShell("Write-Output 'sentinel-ok'"), TimeSpan.FromSeconds(10));
Require(success.Outcome == ProcessExecutionOutcome.Succeeded && success.StandardOutput.Contains("sentinel-ok", StringComparison.Ordinal), "Normal success was not captured correctly.");
Console.WriteLine("Normal success: PASS");

Console.WriteLine("--- Scenario 2: nonzero exit ---");
var nonzero = await BoundedProcessRunner.RunAsync(PowerShell("Write-Error 'expected'; exit 7"), TimeSpan.FromSeconds(10));
Require(nonzero.Outcome == ProcessExecutionOutcome.NonZeroExit && nonzero.ExitCode == 7, "Nonzero exit was not surfaced correctly.");
Console.WriteLine("Nonzero exit: PASS");

Console.WriteLine("--- Scenario 3: simultaneous heavy stdout/stderr ---");
var heavy = await BoundedProcessRunner.RunAsync(
    PowerShell("1..4000 | ForEach-Object { [Console]::Out.WriteLine(('O' * 128)); [Console]::Error.WriteLine(('E' * 128)) }"),
    TimeSpan.FromSeconds(20),
    maxOutputChars: 1_000_000);
Require(heavy.Outcome == ProcessExecutionOutcome.Succeeded, $"Heavy dual-stream output failed: {heavy.Outcome}.");
Require(heavy.StandardOutput.Length > 100_000 && heavy.StandardError.Length > 100_000, "Heavy dual-stream output was not drained concurrently.");
Console.WriteLine("Heavy dual-stream drain: PASS");

Console.WriteLine("--- Scenario 4: timeout ---");
var timedOut = await BoundedProcessRunner.RunAsync(PowerShell("Start-Sleep -Seconds 30"), TimeSpan.FromMilliseconds(700));
Require(timedOut.Outcome == ProcessExecutionOutcome.TimedOut, $"Expected timeout, got {timedOut.Outcome}.");
Console.WriteLine("Wall-clock timeout: PASS");

Console.WriteLine("--- Scenario 5: cancellation ---");
using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700)))
{
    var canceled = await BoundedProcessRunner.RunAsync(PowerShell("Start-Sleep -Seconds 30"), TimeSpan.FromSeconds(30), cts.Token);
    Require(canceled.Outcome == ProcessExecutionOutcome.Canceled, $"Expected canceled, got {canceled.Outcome}.");
}
Console.WriteLine("Caller cancellation: PASS");

Console.WriteLine("--- Scenario 6: bounded output ---");
var capped = await BoundedProcessRunner.RunAsync(
    PowerShell("[Console]::Out.Write(('X' * 200000)); [Console]::Error.Write(('Y' * 200000))"),
    TimeSpan.FromSeconds(10),
    maxOutputChars: 4096);
Require(capped.Outcome == ProcessExecutionOutcome.Succeeded, $"Capped output process failed: {capped.Outcome}.");
Require(capped.OutputTruncated, "Large output was not reported as truncated.");
Require(capped.StandardOutput.Length <= 4096 && capped.StandardError.Length <= 4096, "Captured output exceeded configured cap.");
Console.WriteLine("Output cap: PASS");

Console.WriteLine("RESULT: PASS");
