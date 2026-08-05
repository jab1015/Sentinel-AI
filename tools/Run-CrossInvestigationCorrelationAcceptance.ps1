$ErrorActionPreference = 'Stop'

Write-Host '=== Sentinel AI Cross-Investigation Correlation Acceptance Runner ==='
$project = Join-Path $PSScriptRoot 'Sentinel.CrossInvestigationCorrelationAcceptanceHarness\Sentinel.CrossInvestigationCorrelationAcceptanceHarness.csproj'
Write-Host "Project: $project"
Write-Host ''

dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Cross-investigation correlation acceptance failed with exit code $LASTEXITCODE."
}

Write-Host ''
Write-Host 'Cross-investigation correlation acceptance completed successfully.'
