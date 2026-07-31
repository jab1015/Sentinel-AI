param(
    [ValidateSet(1, 8)]
    [int]$Hours = 1,

    [ValidateRange(5, 300)]
    [int]$SampleSeconds = 30,

    [string]$ProcessName = "Sentinel.App"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$duration = [TimeSpan]::FromHours($Hours)
$startedAt = Get-Date
$endsAt = $startedAt.Add($duration)
$repoRoot = Split-Path -Parent $PSScriptRoot
$resultsDirectory = Join-Path $repoRoot "artifacts\stability"
New-Item -ItemType Directory -Force -Path $resultsDirectory | Out-Null

$stamp = $startedAt.ToString("yyyyMMdd-HHmmss")
$csvPath = Join-Path $resultsDirectory "stability-$($Hours)h-$stamp.csv"
$summaryPath = Join-Path $resultsDirectory "stability-$($Hours)h-$stamp.txt"

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $process) {
    throw "Sentinel process '$ProcessName' is not running. Start Sentinel AI before running this test."
}

$initialId = $process.Id
$initialWorkingSetMb = [math]::Round($process.WorkingSet64 / 1MB, 2)
$initialPrivateMb = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
$initialHandles = $process.HandleCount
$initialThreads = $process.Threads.Count

$samples = New-Object System.Collections.Generic.List[object]
$failureReason = $null

Write-Host "Sentinel AI stability test started: $Hours hour(s)."
Write-Host "Process: $ProcessName ($initialId)"
Write-Host "Results: $resultsDirectory"

while ((Get-Date) -lt $endsAt) {
    $now = Get-Date
    $current = Get-Process -Id $initialId -ErrorAction SilentlyContinue

    if ($null -eq $current) {
        $failureReason = "Sentinel process exited before the stability interval completed."
        break
    }

    $responding = $true
    try {
        $responding = $current.Responding
    }
    catch {
        $responding = $true
    }

    if (-not $responding) {
        $failureReason = "Sentinel stopped responding during the stability interval."
    }

    $samples.Add([pscustomobject]@{
        Timestamp = $now.ToString("o")
        ProcessId = $current.Id
        Responding = $responding
        WorkingSetMB = [math]::Round($current.WorkingSet64 / 1MB, 2)
        PrivateMemoryMB = [math]::Round($current.PrivateMemorySize64 / 1MB, 2)
        HandleCount = $current.HandleCount
        ThreadCount = $current.Threads.Count
        TotalProcessorSeconds = [math]::Round($current.TotalProcessorTime.TotalSeconds, 2)
    })

    if ($null -ne $failureReason) {
        break
    }

    Start-Sleep -Seconds $SampleSeconds
}

$samples | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $csvPath

$completedAt = Get-Date
$completedDuration = $completedAt - $startedAt
$lastSample = $samples | Select-Object -Last 1

if ($null -eq $lastSample -and $null -eq $failureReason) {
    $failureReason = "No stability samples were recorded."
}

$peakWorkingSetMb = if ($samples.Count -gt 0) { ($samples | Measure-Object WorkingSetMB -Maximum).Maximum } else { 0 }
$peakPrivateMb = if ($samples.Count -gt 0) { ($samples | Measure-Object PrivateMemoryMB -Maximum).Maximum } else { 0 }
$peakHandles = if ($samples.Count -gt 0) { ($samples | Measure-Object HandleCount -Maximum).Maximum } else { 0 }
$peakThreads = if ($samples.Count -gt 0) { ($samples | Measure-Object ThreadCount -Maximum).Maximum } else { 0 }

$memoryGrowthMb = if ($null -ne $lastSample) { [math]::Round($lastSample.PrivateMemoryMB - $initialPrivateMb, 2) } else { 0 }
$handleGrowth = if ($null -ne $lastSample) { $lastSample.HandleCount - $initialHandles } else { 0 }
$threadGrowth = if ($null -ne $lastSample) { $lastSample.ThreadCount - $initialThreads } else { 0 }

$passed = $null -eq $failureReason -and $completedDuration.TotalMinutes -ge (($Hours * 60) - 1)
$status = if ($passed) { "PASS" } else { "FAIL" }
$failureText = if ($null -eq $failureReason) { "None" } else { $failureReason }

$summary = @"
Sentinel AI Stability Test
Status: $status
Requested duration: $Hours hour(s)
Observed duration: $([math]::Round($completedDuration.TotalMinutes, 2)) minutes
Started: $($startedAt.ToString("o"))
Completed: $($completedAt.ToString("o"))
Process: $ProcessName
Process ID: $initialId
Samples: $($samples.Count)
Initial working set: $initialWorkingSetMb MB
Peak working set: $peakWorkingSetMb MB
Initial private memory: $initialPrivateMb MB
Peak private memory: $peakPrivateMb MB
Private memory growth: $memoryGrowthMb MB
Initial handles: $initialHandles
Peak handles: $peakHandles
Handle growth: $handleGrowth
Initial threads: $initialThreads
Peak threads: $peakThreads
Thread growth: $threadGrowth
Failure: $failureText
CSV evidence: $csvPath
"@

$summary | Set-Content -Encoding UTF8 -Path $summaryPath
Write-Host $summary

if (-not $passed) {
    exit 1
}

exit 0
