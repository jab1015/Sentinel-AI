$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "tools\Sentinel.AdaptiveDiscoveryDiagnosticsAcceptanceHarness\Sentinel.AdaptiveDiscoveryDiagnosticsAcceptanceHarness.csproj"
Write-Host "=== Sentinel AI Adaptive Discovery Diagnostics Acceptance Runner ==="
Write-Host "Project: $project"
Write-Host ""
dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ""
Write-Host "Adaptive discovery diagnostics acceptance completed successfully."
