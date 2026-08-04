param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$tests = @(
    @{ Name = 'Process containment'; Project = 'tools/Sentinel.ContainmentHarness/Sentinel.ContainmentHarness.csproj' },
    @{ Name = 'Firewall containment'; Project = 'tools/Sentinel.FirewallContainmentHarness/Sentinel.FirewallContainmentHarness.csproj' },
    @{ Name = 'Quarantine and restore'; Project = 'tools/Sentinel.QuarantineAcceptanceHarness/Sentinel.QuarantineAcceptanceHarness.csproj' }
)

Write-Host '=== Sentinel AI Phase 8.4 Containment Acceptance ==='
Write-Host ''

$failed = @()

foreach ($test in $tests) {
    $projectPath = Join-Path $root $test.Project
    Write-Host "--- $($test.Name) ---"

    if (-not (Test-Path $projectPath)) {
        Write-Host "FAIL: Project not found: $projectPath"
        $failed += $test.Name
        Write-Host ''
        continue
    }

    & dotnet run --project $projectPath -c Release
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0) {
        Write-Host "PASS: $($test.Name)"
    }
    else {
        Write-Host "FAIL: $($test.Name) (exit code $exitCode)"
        $failed += $test.Name
    }

    Write-Host ''
}

if ($failed.Count -eq 0) {
    Write-Host 'RESULT: PASS - Phase 8.4 containment execution and reversal harnesses passed.'
    exit 0
}

$failedNames = $failed -join ', '
Write-Host "RESULT: FAIL - $($failed.Count) acceptance area(s) failed: $failedNames"
exit 1
