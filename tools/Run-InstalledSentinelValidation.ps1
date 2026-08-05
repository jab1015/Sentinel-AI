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
        Check = $Name
        Result = if ($Passed) { 'PASS' } else { 'FAIL' }
        Evidence = $Evidence
    })
}

$results = [System.Collections.Generic.List[object]]::new()
$packageName = '07414ecb-83de-4656-bf2d-b299d64ce5c5'

Write-Host '=== Sentinel AI Installed Application Validation ==='
Write-Host "Observation window: $ObservationSeconds seconds"
Write-Host ''

$package = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue | Select-Object -First 1
Add-Result $results 'Sentinel package installed' ($null -ne $package) $(
    if ($package) { "Version=$($package.Version); PackageFullName=$($package.PackageFullName)" } else { 'Sentinel AI package is not installed for the current user.' }
)

$publisherPassed = $false
$publisherEvidence = 'Package unavailable.'
$startupPassed = $false
$startupEvidence = 'Package unavailable.'
if ($package) {
    $publisherPassed = $package.Publisher -eq 'CN=Modern Methods'
    $publisherEvidence = "Publisher=$($package.Publisher)"

    try {
        [xml]$manifest = Get-AppxPackageManifest -Package $package.PackageFullName -ErrorAction Stop
        $ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
        $ns.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
        $ns.AddNamespace('desktop', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10')
        $startupNode = $manifest.SelectSingleNode("/f:Package/f:Applications/f:Application/f:Extensions/desktop:Extension[@Category='windows.startupTask']/desktop:StartupTask[@TaskId='SentinelStartupTask']", $ns)
        $startupPassed = $null -ne $startupNode
        $startupEvidence = if ($startupPassed) { 'SentinelStartupTask is declared in the installed manifest.' } else { 'SentinelStartupTask was not found in the installed manifest.' }
    } catch {
        $startupEvidence = $_.Exception.Message
    }
}
Add-Result $results 'Installed publisher is Modern Methods' $publisherPassed $publisherEvidence
Add-Result $results 'Installed startup task declared' $startupPassed $startupEvidence

$process = Get-Process -Name 'Sentinel.App' -ErrorAction SilentlyContinue | Select-Object -First 1
Add-Result $results 'Installed Sentinel process running' ($null -ne $process) $(
    if ($process) { "PID=$($process.Id); StartTime=$($process.StartTime)" } else { 'Sentinel.App is not running.' }
)

if ($process) {
    $initialPid = $process.Id
    Start-Sleep -Seconds ([Math]::Max(1, $ObservationSeconds))
    $stillRunning = Get-Process -Id $initialPid -ErrorAction SilentlyContinue
    Add-Result $results 'Sentinel remains running during observation' ($null -ne $stillRunning) $(
        if ($stillRunning) { "PID $initialPid remained active for $ObservationSeconds seconds." } else { "Sentinel exited during the $ObservationSeconds-second observation." }
    )
} else {
    Add-Result $results 'Sentinel remains running during observation' $false 'Skipped because Sentinel.App was not running at test start.'
}

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

$firewallPassed = $false
$firewallEvidence = 'Unable to query Firewall.'
try {
    $profiles = @(Get-NetFirewallProfile -ErrorAction Stop)
    $disabled = @($profiles | Where-Object { -not $_.Enabled })
    $firewallPassed = $disabled.Count -eq 0
    $firewallEvidence = ($profiles | ForEach-Object { "$($_.Name)=$($_.Enabled)" }) -join '; '
} catch {
    $firewallEvidence = $_.Exception.Message
}
Add-Result $results 'Windows Firewall profiles active' $firewallPassed $firewallEvidence

$tcpPassed = $false
$tcpEvidenceText = 'Unable to query TCP telemetry.'
try {
    $tcp = @(Get-NetTCPConnection -ErrorAction Stop)
    $tcpPassed = $true
    $established = @($tcp | Where-Object State -eq 'Established').Count
    $listening = @($tcp | Where-Object State -eq 'Listen').Count
    $tcpEvidenceText = "Established=$established; Listening=$listening"
} catch {
    $tcpEvidenceText = $_.Exception.Message
}
Add-Result $results 'Windows network telemetry available' $tcpPassed $tcpEvidenceText

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
$logPassed = -not [string]::IsNullOrWhiteSpace($logPath)
Add-Result $results 'Sentinel diagnostic log available' $logPassed $(if ($logPassed) { $logPath } else { 'sentinel.log not found.' })

if ($logPassed) {
    $tail = @(Get-Content $logPath -Tail 500 -ErrorAction SilentlyContinue)
    $fatal = @($tail | Where-Object { $_ -match '\| ERROR \| (UnhandledException|ApplicationLaunchFailure) \|' })
    Add-Result $results 'No recent fatal Sentinel runtime error' ($fatal.Count -eq 0) $(
        if ($fatal.Count -eq 0) { 'No recent fatal Sentinel boundary/startup errors found.' } else { $fatal[-1] }
    )
}

$memoryPath = Join-Path $env:LOCALAPPDATA 'SentinelAI\InvestigationMemory\investigations.json'
$memoryExists = Test-Path $memoryPath
Add-Result $results 'Persistent investigation memory available' $memoryExists $(
    if ($memoryExists) { $memoryPath } else { 'Persistent investigation store not found for the current user.' }
)

if ($memoryExists) {
    try {
        $records = @(Get-Content $memoryPath -Raw | ConvertFrom-Json)
        $recordCount = $records.Count
        $suppressed = @($records | Where-Object { $_.notificationsSuppressed -eq $true }).Count
        Add-Result $results 'Persistent investigation memory readable' $true "Records=$recordCount; SilentlyMonitored=$suppressed"
    } catch {
        Add-Result $results 'Persistent investigation memory readable' $false $_.Exception.Message
    }
}

$failed = @($results | Where-Object Result -eq 'FAIL')
''
'=== INSTALLED SENTINEL VALIDATION SUMMARY ==='
$results | Format-Table -AutoSize -Wrap
''
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)"
"FAIL: $($failed.Count)"

if ($failed.Count -gt 0) {
    Write-Host 'RESULT: FAIL' -ForegroundColor Red
    exit 1
}

Write-Host 'RESULT: PASS' -ForegroundColor Green
Write-Host 'Installed Sentinel runtime validation completed successfully.'
exit 0
