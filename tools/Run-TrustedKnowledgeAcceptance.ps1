$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "tools\Sentinel.TrustedKnowledgeAcceptanceHarness\Sentinel.TrustedKnowledgeAcceptanceHarness.csproj"
Write-Host "=== Sentinel AI Trusted Knowledge Acceptance Runner ==="
Write-Host "Project: $project"
Write-Host ""
dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ""
Write-Host "Trusted knowledge acceptance completed successfully."
