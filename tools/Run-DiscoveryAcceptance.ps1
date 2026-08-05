$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tools\Sentinel.DiscoveryAcceptanceHarness\Sentinel.DiscoveryAcceptanceHarness.csproj'

Write-Host '=== Sentinel AI Discovery Acceptance Runner ===' -ForegroundColor Cyan
Write-Host "Project: $project"
Write-Host ''

dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Discovery acceptance failed with exit code $LASTEXITCODE."
}

Write-Host ''
Write-Host 'Discovery acceptance completed successfully.' -ForegroundColor Green
