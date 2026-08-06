$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "tools\Sentinel.FriendlyValueActivityAcceptanceHarness\Sentinel.FriendlyValueActivityAcceptanceHarness.csproj"

Write-Host "=== Sentinel AI Friendly Value Activity Acceptance Runner ==="
Write-Host "Project: $project"
Write-Host ""

dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Friendly value Activity Center acceptance failed."
}

Write-Host ""
Write-Host "Friendly value Activity Center acceptance completed successfully."
