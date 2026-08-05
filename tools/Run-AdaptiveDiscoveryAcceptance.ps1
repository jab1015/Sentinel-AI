$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "tools\Sentinel.AdaptiveDiscoveryAcceptanceHarness\Sentinel.AdaptiveDiscoveryAcceptanceHarness.csproj"

Write-Host "=== Sentinel AI Adaptive Discovery Acceptance Runner ==="
Write-Host "Project: $project"
Write-Host ""

dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Adaptive discovery acceptance completed successfully."
