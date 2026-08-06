$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

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

Write-Host "=== Sentinel AI Final Production Regression ==="
Write-Host "Repository: $repoRoot"
Write-Host "Suites: $($acceptanceSuites.Count)"
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

    & $path
    if ($LASTEXITCODE -eq 0) {
        Write-Host "SUITE RESULT: PASS" -ForegroundColor Green
        $passed++
        $results += [pscustomobject]@{ Suite = $suite; Result = "PASS" }
    }
    else {
        Write-Host "SUITE RESULT: FAIL (exit code $LASTEXITCODE)" -ForegroundColor Red
        $failed++
        $results += [pscustomobject]@{ Suite = $suite; Result = "FAIL" }
    }

    Write-Host ""
}

Write-Host "=== FINAL PRODUCTION REGRESSION SUMMARY ==="
$results | Format-Table -AutoSize
Write-Host "Passed: $passed"
Write-Host "Failed: $failed"

if ($failed -gt 0) {
    Write-Host "OVERALL RESULT: FAIL" -ForegroundColor Red
    exit 1
}

Write-Host "OVERALL RESULT: PASS" -ForegroundColor Green
Write-Host "All available production regression suites completed successfully."
exit 0
