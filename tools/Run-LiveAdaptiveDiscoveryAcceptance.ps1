$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "tools\Sentinel.LiveAdaptiveDiscoveryAcceptanceHarness\Sentinel.LiveAdaptiveDiscoveryAcceptanceHarness.csproj"
Write-Host "=== Sentinel AI Live Adaptive Discovery Acceptance Runner ==="
Write-Host "Project: $project"
Write-Host ""
dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ""
Write-Host "Live adaptive discovery acceptance completed successfully."
