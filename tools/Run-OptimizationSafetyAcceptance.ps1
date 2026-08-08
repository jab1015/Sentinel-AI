$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Sentinel.OptimizationSafetyAcceptanceHarness\Sentinel.OptimizationSafetyAcceptanceHarness.csproj'

Write-Host '=== Sentinel AI Optimization Safety Acceptance Runner ==='
Write-Host "Project: $project"
Write-Host ''

dotnet run --project $project --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Optimization safety acceptance failed with exit code $LASTEXITCODE."
}

Write-Host ''
Write-Host 'Optimization safety acceptance completed successfully.'
