$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Sentinel.RemediationOutcomeAcceptanceHarness\Sentinel.RemediationOutcomeAcceptanceHarness.csproj'

Write-Host '=== Sentinel AI Remediation Outcome Acceptance Runner ==='
Write-Host "Project: $project"
Write-Host ''

dotnet run --project $project --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Remediation outcome acceptance failed with exit code $LASTEXITCODE."
}

Write-Host ''
Write-Host 'Remediation outcome acceptance completed successfully.'
