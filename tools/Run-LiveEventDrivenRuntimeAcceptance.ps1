$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "tools\Sentinel.LiveEventDrivenRuntimeAcceptanceHarness\Sentinel.LiveEventDrivenRuntimeAcceptanceHarness.csproj"

Write-Host "=== Sentinel AI Live Event-Driven Runtime Acceptance Runner ==="
Write-Host "Project: $project"
Write-Host ""

dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Live event-driven runtime acceptance failed."
}

Write-Host ""
Write-Host "Live event-driven runtime acceptance completed successfully."
