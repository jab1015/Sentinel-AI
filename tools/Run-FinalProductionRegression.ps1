$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$perSuiteTimeout = [TimeSpan]::FromMinutes(20)

$acceptanceSuites = @(
    "Run-DiscoveryAcceptance.ps1",
    "Run-PersistentInvestigationAcceptance.ps1",
    "Run-LivePersistentExceptionAcceptance.ps1",
    "Run-CrossInvestigationCorrelationAcceptance.ps1",
    "Run-TrustedKnowledgeAcceptance.ps1",
    "Run-AdaptiveDiscoveryAcceptance.ps1",
    "Run-LiveAdaptiveDiscoveryAcceptance.ps1",
    "Run-AdaptiveDiscoveryDiagnosticsAcceptance.ps1",
    "Run-EventDrivenDiscoveryAcceptance.ps1",
    "Run-LiveEventDrivenDiscoveryAcceptance.ps1",
    "Run-LiveEventDrivenRuntimeAcceptance.ps1",
    "Run-EventDrivenDiscoveryDiagnosticsAcceptance.ps1",
    "Run-QuarantineAcceptance.ps1",
    "Run-FriendlyValueSummaryAcceptance.ps1",
    "Run-FriendlyValueActivityAcceptance.ps1"
)

function Resolve-PowerShellHost {
    try {
        $current = (Get-Process -Id $PID -ErrorAction Stop).Path
        if (-not [string]::IsNullOrWhiteSpace($current) -and (Test-Path $current)) {
            return $current
        }
    }
    catch { }

    $pwsh = Get-Command pwsh.exe -ErrorAction SilentlyContinue
    if ($null -ne $pwsh) { return $pwsh.Source }

    $windowsPowerShell = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($null -ne $windowsPowerShell) { return $windowsPowerShell.Source }

    throw "No PowerShell host executable could be resolved."
}

function Invoke-IsolatedSuite([string]$Path, [TimeSpan]$Timeout) {
    $hostPath = Resolve-PowerShellHost
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $hostPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $false
    [void]$startInfo.ArgumentList.Add("-NoLogo")
    [void]$startInfo.ArgumentList.Add("-NoProfile")
    [void]$startInfo.ArgumentList.Add("-NonInteractive")
    if ([System.IO.Path]::GetFileName($hostPath).Equals("powershell.exe", [System.StringComparison]::OrdinalIgnoreCase)) {
        [void]$startInfo.ArgumentList.Add("-ExecutionPolicy")
        [void]$startInfo.ArgumentList.Add("Bypass")
    }
    [void]$startInfo.ArgumentList.Add("-File")
    [void]$startInfo.ArgumentList.Add($Path)

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            return [pscustomobject]@{ ExitCode = $null; TimedOut = $false; LaunchFailed = $true }
        }

        if (-not $process.WaitForExit([int]$Timeout.TotalMilliseconds)) {
            try { $process.Kill($true) } catch { }
            try { [void]$process.WaitForExit(5000) } catch { }
            return [pscustomobject]@{ ExitCode = $null; TimedOut = $true; LaunchFailed = $false }
        }

        return [pscustomobject]@{ ExitCode = $process.ExitCode; TimedOut = $false; LaunchFailed = $false }
    }
    catch {
        return [pscustomobject]@{ ExitCode = $null; TimedOut = $false; LaunchFailed = $true }
    }
    finally {
        $process.Dispose()
    }
}

Write-Host "=== Sentinel AI Final Production Regression ==="
Write-Host "Repository: $repoRoot"
Write-Host "Suites: $($acceptanceSuites.Count)"
Write-Host "Per-suite timeout: $($perSuiteTimeout.TotalMinutes) minutes"
Write-Host ""

$passed = 0
$failed = 0
$results = @()

foreach ($suite in $acceptanceSuites) {
    $path = Join-Path $PSScriptRoot $suite
    Write-Host "============================================================"
    Write-Host "RUNNING: $suite"
    Write-Host "============================================================"

    if (-not (Test-Path $path)) {
        Write-Host "RESULT: FAIL - runner not found: $path" -ForegroundColor Red
        $failed++
        $results += [pscustomobject]@{ Suite = $suite; Result = "FAIL - MISSING" }
        continue
    }

    $execution = Invoke-IsolatedSuite -Path $path -Timeout $perSuiteTimeout
    if ($execution.LaunchFailed) {
        Write-Host "SUITE RESULT: FAIL - child PowerShell could not be launched" -ForegroundColor Red
        $failed++
        $results += [pscustomobject]@{ Suite = $suite; Result = "FAIL - LAUNCH" }
    }
    elseif ($execution.TimedOut) {
        Write-Host "SUITE RESULT: FAIL - exceeded $($perSuiteTimeout.TotalMinutes)-minute timeout" -ForegroundColor Red
        $failed++
        $results += [pscustomobject]@{ Suite = $suite; Result = "FAIL - TIMEOUT" }
    }
    elseif ($execution.ExitCode -eq 0) {
        Write-Host "SUITE RESULT: PASS" -ForegroundColor Green
        $passed++
        $results += [pscustomobject]@{ Suite = $suite; Result = "PASS" }
    }
    else {
        Write-Host "SUITE RESULT: FAIL (exit code $($execution.ExitCode))" -ForegroundColor Red
        $failed++
        $results += [pscustomobject]@{ Suite = $suite; Result = "FAIL ($($execution.ExitCode))" }
    }

    Write-Host ""
}

Write-Host "=== FINAL PRODUCTION REGRESSION SUMMARY ==="
$results | Format-Table -AutoSize
Write-Host "Passed: $passed"
Write-Host "Failed: $failed"

if ($passed + $failed -ne $acceptanceSuites.Count) {
    Write-Host "OVERALL RESULT: FAIL - suite accounting mismatch" -ForegroundColor Red
    exit 1
}

if ($failed -gt 0) {
    Write-Host "OVERALL RESULT: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "OVERALL RESULT: PASS" -ForegroundColor Green
Write-Host "All configured production regression suites completed successfully in isolated child PowerShell processes."
exit 0
