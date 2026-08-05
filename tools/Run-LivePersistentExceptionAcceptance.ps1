$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'Sentinel.LivePersistentExceptionAcceptanceHarness\Sentinel.LivePersistentExceptionAcceptanceHarness.csproj'

Write-Host '=== Sentinel AI Live Persistent Exception Acceptance Runner ==='
Write-Host "Project: $project"
Write-Host ''

dotnet run --project $project --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Live persistent exception acceptance failed with exit code $LASTEXITCODE."
}

Write-Host ''
Write-Host 'Live persistent exception acceptance completed successfully.' -ForegroundColor Green
