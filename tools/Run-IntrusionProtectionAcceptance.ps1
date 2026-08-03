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

# Verify the installed package declares Sentinel's supported Windows startup task.
# Actual automatic launch is verified separately by the reboot/sign-in acceptance step.
$packageName = '07414ecb-83de-4656-bf2d-b299d64ce5c5'
$package = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue | Select-Object -First 1
$startupDeclared = $false
$startupEvidence = 'Sentinel package or startupTask declaration was not found.'
if ($package) {
    try {
        [xml]$manifest = Get-AppxPackageManifest -Package $package.PackageFullName -ErrorAction Stop
        $namespace = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
        $namespace.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
        $namespace.AddNamespace('desktop', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10')
        $startupNode = $manifest.SelectSingleNode("/f:Package/f:Applications/f:Application/f:Extensions/desktop:Extension[@Category='windows.startupTask']/desktop:StartupTask[@TaskId='SentinelStartupTask']", $namespace)
        $startupDeclared = $null -ne $startupNode
        $startupEvidence = if ($startupDeclared) {
            "Package $($package.Version) declares SentinelStartupTask; reboot/sign-in verification is still required."
        } else {
            "Installed package $($package.Version) does not declare SentinelStartupTask."
        }
    } catch {
        $startupEvidence = $_.Exception.Message
    }
}
Add-Result $results 'Windows startup task packaged' $startupDeclared $startupEvidence

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
try {
    $tcpEvidence = @(Get-NetTCPConnection -State Established -ErrorAction Stop)
    Add-Result $results 'Windows TCP telemetry available' $true "Established TCP connections observed: $($tcpEvidence.Count)"
} catch {
    Add-Result $results 'Windows TCP telemetry available' $false $_.Exception.Message
}

# Locate Sentinel diagnostics in either the unpackaged path or the packaged LocalCache path.
$logCandidates = [System.Collections.Generic.List[string]]::new()
$logCandidates.Add((Join-Path $env:LOCALAPPDATA 'Modern Methods\Sentinel AI\Logs\sentinel.log'))

$packagesRoot = Join-Path $env:LOCALAPPDATA 'Packages'
if (Test-Path $packagesRoot) {
    Get-ChildItem $packagesRoot -Directory -Filter "$packageName*" -ErrorAction SilentlyContinue | ForEach-Object {
        $logCandidates.Add((Join-Path $_.FullName 'LocalCache\Local\Modern Methods\Sentinel AI\Logs\sentinel.log'))
        $logCandidates.Add((Join-Path $_.FullName 'LocalState\Modern Methods\Sentinel AI\Logs\sentinel.log'))
    }
}

$logPath = $logCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
$logExists = -not [string]::IsNullOrWhiteSpace($logPath)
Add-Result $results 'Sentinel diagnostic log available' $logExists $(
    if ($logExists) { $logPath } else { 'sentinel.log was not found in unpackaged or packaged application data.' }
)

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
