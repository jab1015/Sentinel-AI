param(
    [int]$ObservationSeconds = 60
)

$ErrorActionPreference = 'Stop'

function Add-Result {
    param(
        [System.Collections.Generic.List[object]]$Results,
        [string]$Name,
        [bool]$Passed,
        [string]$Evidence
    )

    $Results.Add([pscustomobject]@{
        Check    = $Name
        Result   = if ($Passed) { 'PASS' } else { 'FAIL' }
        Evidence = $Evidence
    })
}

$results = [System.Collections.Generic.List[object]]::new()
$start = Get-Date

# Sentinel process must already be running for runtime acceptance.
$sentinel = Get-Process -Name 'Sentinel.App' -ErrorAction SilentlyContinue | Select-Object -First 1
Add-Result $results 'Sentinel process running' ($null -ne $sentinel) $(
    if ($sentinel) { "PID $($sentinel.Id)" } else { 'Sentinel.App is not running.' }
)

# Verify startup registration written by the installed package execution path.
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupValue = $null
try {
    $startupValue = (Get-ItemProperty -Path $runPath -Name 'Sentinel AI' -ErrorAction Stop).'Sentinel AI'
} catch { }
$startupValid = -not [string]::IsNullOrWhiteSpace($startupValue) -and $startupValue -match 'shell:AppsFolder'
Add-Result $results 'Windows sign-in startup registered' $startupValid $(
    if ($startupValid) { $startupValue } else { 'Sentinel AI startup registration was not found or was not package-based.' }
)

# Defender runtime status.
$defenderPassed = $false
$defenderEvidence = 'Unable to query Defender.'
try {
    $mp = Get-MpComputerStatus -ErrorAction Stop
    $defenderPassed = [bool]$mp.AntivirusEnabled -and [bool]$mp.RealTimeProtectionEnabled
    $defenderEvidence = "AntivirusEnabled=$($mp.AntivirusEnabled); RealTimeProtectionEnabled=$($mp.RealTimeProtectionEnabled)"
} catch {
    $defenderEvidence = $_.Exception.Message
}
Add-Result $results 'Defender protection active' $defenderPassed $defenderEvidence

# Firewall profiles should all be enabled unless the environment is intentionally managed otherwise.
$firewallPassed = $false
$firewallEvidence = 'Unable to query Windows Firewall.'
try {
    $profiles = Get-NetFirewallProfile -ErrorAction Stop
    $disabled = @($profiles | Where-Object { -not $_.Enabled })
    $firewallPassed = $disabled.Count -eq 0
    $firewallEvidence = ($profiles | ForEach-Object { "$($_.Name)=$($_.Enabled)" }) -join '; '
} catch {
    $firewallEvidence = $_.Exception.Message
}
Add-Result $results 'Windows Firewall profiles active' $firewallPassed $firewallEvidence

# Observe Sentinel over time. This catches immediate exits and verifies the process remains alive.
if ($sentinel) {
    Start-Sleep -Seconds ([Math]::Max(1, $ObservationSeconds))
    $stillRunning = Get-Process -Id $sentinel.Id -ErrorAction SilentlyContinue
    Add-Result $results 'Sentinel remains running during observation' ($null -ne $stillRunning) $(
        if ($stillRunning) { "Observed for $ObservationSeconds seconds; PID $($sentinel.Id) remained active." } else { "Sentinel exited during the $ObservationSeconds-second observation." }
    )
} else {
    Add-Result $results 'Sentinel remains running during observation' $false 'Skipped because Sentinel.App was not running at test start.'
}

# Confirm that Windows exposes active TCP evidence while Sentinel is running.
$tcpEvidence = @()
try {
    $tcpEvidence = @(Get-NetTCPConnection -State Established -ErrorAction Stop)
    Add-Result $results 'Windows TCP telemetry available' $true "Established TCP connections observed: $($tcpEvidence.Count)"
} catch {
    Add-Result $results 'Windows TCP telemetry available' $false $_.Exception.Message
}

# Sentinel production log should exist after installed/runtime launch and should not show a recent unhandled exception.
$logPath = Join-Path $env:LOCALAPPDATA 'Modern Methods\Sentinel AI\Logs\sentinel.log'
$logExists = Test-Path $logPath
Add-Result $results 'Sentinel diagnostic log available' $logExists $(if ($logExists) { $logPath } else { 'sentinel.log was not found.' })

if ($logExists) {
    $tail = @(Get-Content $logPath -Tail 300 -ErrorAction SilentlyContinue)
    $recentUnhandled = @($tail | Where-Object { $_ -match '\| ERROR \| (UnhandledException|ApplicationLaunchFailure) \|' })
    Add-Result $results 'No recent Sentinel fatal startup/runtime error' ($recentUnhandled.Count -eq 0) $(
        if ($recentUnhandled.Count -eq 0) { 'No fatal Sentinel boundary/startup errors found in the recent log tail.' }
        else { $recentUnhandled[-1] }
    )
}

$failed = @($results | Where-Object { $_.Result -eq 'FAIL' })
$elapsed = (Get-Date) - $start

''
'=== Sentinel AI Phase 8 Acceptance Harness ==='
$results | Format-Table -AutoSize -Wrap
''
"Observed: $([Math]::Round($elapsed.TotalSeconds,1)) seconds"
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)"
"FAIL: $($failed.Count)"

if ($failed.Count -gt 0) {
    Write-Host 'RESULT: FAIL' -ForegroundColor Red
    exit 1
}

Write-Host 'RESULT: PASS' -ForegroundColor Green
exit 0
