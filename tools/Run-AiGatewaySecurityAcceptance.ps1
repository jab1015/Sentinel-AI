$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'Sentinel.AiGatewaySecurityAcceptanceHarness/Sentinel.AiGatewaySecurityAcceptanceHarness.csproj'

Write-Host '=== Sentinel AI Gateway Security Acceptance ==='
dotnet run --project $project --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host 'SUITE RESULT: PASS'
