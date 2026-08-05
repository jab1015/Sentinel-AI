$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'Sentinel.PersistentInvestigationAcceptanceHarness\Sentinel.PersistentInvestigationAcceptanceHarness.csproj'

Write-Host '=== Sentinel AI Persistent Investigation Acceptance Runner ==='
Write-Host "Project: $project"
Write-Host ''

dotnet run --project $project --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Persistent investigation acceptance failed with exit code $LASTEXITCODE."
}

Write-Host ''
Write-Host 'Persistent investigation acceptance completed successfully.'
