$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "tools\Sentinel.LiveEventDrivenDiscoveryAcceptanceHarness\Sentinel.LiveEventDrivenDiscoveryAcceptanceHarness.csproj"
Write-Host "=== Sentinel AI Live Event-Driven Discovery Acceptance Runner ==="
Write-Host "Project: $project"
Write-Host ""
dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ""
Write-Host "Live event-driven Discovery acceptance completed successfully."
