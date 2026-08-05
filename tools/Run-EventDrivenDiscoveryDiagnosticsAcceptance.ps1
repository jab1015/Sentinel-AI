$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "tools\Sentinel.EventDrivenDiscoveryDiagnosticsAcceptanceHarness\Sentinel.EventDrivenDiscoveryDiagnosticsAcceptanceHarness.csproj"

Write-Host "=== Sentinel AI Event-Driven Discovery Diagnostics Acceptance Runner ==="
Write-Host "Project: $project"
Write-Host ""

dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Event-driven Discovery diagnostics acceptance failed."
}

Write-Host ""
Write-Host "Event-driven Discovery diagnostics acceptance completed successfully."
