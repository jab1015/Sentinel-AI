$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "tools\Sentinel.EventDrivenDiscoveryAcceptanceHarness\Sentinel.EventDrivenDiscoveryAcceptanceHarness.csproj"
Write-Host "=== Sentinel AI Event-Driven Discovery Acceptance Runner ==="
Write-Host "Project: $project"
Write-Host ""
dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ""
Write-Host "Event-driven discovery acceptance completed successfully."
