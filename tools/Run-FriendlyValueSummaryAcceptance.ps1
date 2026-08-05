$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "tools\Sentinel.FriendlyValueSummaryAcceptanceHarness\Sentinel.FriendlyValueSummaryAcceptanceHarness.csproj"

Write-Host "=== Sentinel AI Friendly Value Summary Acceptance Runner ==="
Write-Host "Project: $project"
Write-Host ""

dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Friendly value summary acceptance failed."
}

Write-Host ""
Write-Host "Friendly value summary acceptance completed successfully."
