$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Sentinel.MaintenanceHistoryAcceptanceHarness\Sentinel.MaintenanceHistoryAcceptanceHarness.csproj'

Write-Host '=== Sentinel AI Maintenance History Acceptance Runner ==='
Write-Host "Project: $project"
Write-Host ''

dotnet run --project $project --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Maintenance history acceptance failed with exit code $LASTEXITCODE."
}

Write-Host ''
Write-Host 'Maintenance history acceptance completed successfully.'
