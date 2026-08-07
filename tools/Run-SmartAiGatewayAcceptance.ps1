param(
    [string]$GatewayUrl = "https://sentinel-ai-gateway-49908265995.us-central1.run.app/v1/analyze"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Sentinel AI Gateway Acceptance ==="
Write-Host "Endpoint: $GatewayUrl"

$healthUrl = $GatewayUrl -replace "/v1/analyze$", "/health"
$health = Invoke-RestMethod -Uri $healthUrl -Method Get
if ($health.status -ne "healthy" -or -not $health.providerConfigured) {
    throw "FAIL: Gateway health/provider configuration is not ready."
}
Write-Host "PASS: Gateway healthy and provider configured."

$payload = @{
    schemaVersion = 1
    purpose = "acceptance-test"
    modelTier = "Economy"
    maximumTotalTokens = 1200
    evidence = @"
SENTINEL_AI_EVIDENCE_V1
purpose: acceptance-test
question: Explain what Sentinel should do when local evidence is insufficient.
rules: use only supplied verified evidence; distinguish fact from inference; do not authorize repairs; request more evidence when insufficient.
facts:
- reason: synthetic acceptance test
- conclusion: local evidence is insufficient
- summary: no verified machine-specific diagnosis exists
- external-summary: authoritative source availability is verified but no machine-specific conclusion has been corroborated
"@
} | ConvertTo-Json -Depth 5

$result = Invoke-RestMethod -Uri $GatewayUrl -Method Post -ContentType "application/json" -Body $payload

if ([string]::IsNullOrWhiteSpace($result.answer)) {
    throw "FAIL: Gateway returned no AI answer."
}
if ($result.provider -ne "OpenAI") {
    throw "FAIL: Unexpected provider '$($result.provider)'."
}
if ($result.inputTokens -le 0 -or $result.outputTokens -le 0) {
    throw "FAIL: Token accounting was not returned."
}
if ($result.inputTokens + $result.outputTokens -gt 1200) {
    throw "FAIL: Token budget exceeded."
}

Write-Host "PASS: OpenAI response received."
Write-Host "Provider: $($result.provider)"
Write-Host "Model: $($result.model)"
Write-Host "Input tokens: $($result.inputTokens)"
Write-Host "Output tokens: $($result.outputTokens)"
Write-Host "Total tokens: $($result.inputTokens + $result.outputTokens)"
Write-Host "Confidence: $($result.confidencePercent)%"
Write-Host "Requires more evidence: $($result.requiresMoreEvidence)"
Write-Host "Answer:"
Write-Host $result.answer
Write-Host "PASS: Smart Sentinel AI gateway acceptance complete."
