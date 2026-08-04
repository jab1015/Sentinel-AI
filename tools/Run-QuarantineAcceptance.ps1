$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'Sentinel.QuarantineAcceptanceHarness\Sentinel.QuarantineAcceptanceHarness.csproj'

if (-not (Test-Path $project)) {
    Write-Error "Quarantine acceptance project not found: $project"
    exit 1
}

Write-Host '=== Sentinel AI Quarantine Acceptance Runner ===' -ForegroundColor Cyan
Write-Host "Project: $project"
Write-Host

dotnet run --project $project -c Release -r win-x64
$exitCode = $LASTEXITCODE

Write-Host
if ($exitCode -eq 0) {
    Write-Host 'Quarantine acceptance completed successfully.' -ForegroundColor Green
} else {
    Write-Host "Quarantine acceptance failed with exit code $exitCode." -ForegroundColor Red
}

exit $exitCode
