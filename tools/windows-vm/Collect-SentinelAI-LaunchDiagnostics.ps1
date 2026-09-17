[CmdletBinding()]
param(
    [int]$LookbackMinutes = 10,
    [switch]$SkipLaunch
)

$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

$packageName = 'ModernMethods.SentinelAI'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputPath = Join-Path (Get-Location) "SentinelAI-LaunchDiagnostics-$timestamp.txt"

function Write-Section {
    param([string]$Title)
    "`r`n===== $Title =====" | Tee-Object -FilePath $outputPath -Append | Write-Host
}

function Write-Lines {
    param([object]$Value)
    ($Value | Out-String -Width 260).TrimEnd() | Tee-Object -FilePath $outputPath -Append | Write-Host
}

"Sentinel AI installed-launch diagnostics" | Set-Content -LiteralPath $outputPath -Encoding utf8
"Collected: $(Get-Date -Format o)" | Add-Content -LiteralPath $outputPath -Encoding utf8
"Computer: $env:COMPUTERNAME" | Add-Content -LiteralPath $outputPath -Encoding utf8
"Windows: $([Environment]::OSVersion.VersionString)" | Add-Content -LiteralPath $outputPath -Encoding utf8

Write-Section 'Installed package'
$installed = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue | Sort-Object Version -Descending | Select-Object -First 1
if ($null -eq $installed) {
    Write-Lines "ERROR: $packageName is not installed for the current user."
    Write-Host "Diagnostics saved to $outputPath"
    exit 2
}

Write-Lines ($installed | Select-Object Name, PackageFullName, PackageFamilyName, Version, Architecture, Publisher, InstallLocation, Status)

$installRoot = $installed.InstallLocation
$exePath = Join-Path $installRoot 'Sentinel.App.exe'
Write-Section 'Installed executable and runtime payload'
Write-Lines "Sentinel.App.exe present: $(Test-Path -LiteralPath $exePath -PathType Leaf)"
if (Test-Path -LiteralPath $exePath -PathType Leaf) {
    $exe = Get-Item -LiteralPath $exePath
    Write-Lines ($exe | Select-Object FullName, Length, LastWriteTime, VersionInfo)
    Write-Lines (Get-AuthenticodeSignature -LiteralPath $exePath | Select-Object Status, StatusMessage, SignerCertificate)
}

$criticalNames = @(
    'Sentinel.App.dll',
    'Sentinel.App.runtimeconfig.json',
    'Sentinel.App.deps.json',
    'System.Text.Json.dll',
    'Microsoft.WindowsAppRuntime.Bootstrap.dll',
    'Microsoft.WindowsAppRuntime.dll',
    'Microsoft.UI.Xaml.dll',
    'hostfxr.dll',
    'hostpolicy.dll',
    'coreclr.dll'
)
foreach ($name in $criticalNames) {
    $matches = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File -Filter $name -ErrorAction SilentlyContinue)
    Write-Lines ("{0}: {1}{2}" -f $name, $matches.Count, $(if ($matches.Count -gt 0) { " -> " + (($matches.FullName | ForEach-Object { $_.Substring($installRoot.Length).TrimStart('\\') }) -join '; ') } else { '' }))
}

Write-Section 'System.Text.Json assembly identity and dependency registration'
$jsonPath = Join-Path $installRoot 'System.Text.Json.dll'
if (Test-Path -LiteralPath $jsonPath -PathType Leaf) {
    try {
        $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($jsonPath)
        $jsonFile = Get-Item -LiteralPath $jsonPath
        Write-Lines ([pscustomobject]@{
            Path = $jsonPath
            AssemblyFullName = $assemblyName.FullName
            AssemblyVersion = $assemblyName.Version
            FileVersion = $jsonFile.VersionInfo.FileVersion
            ProductVersion = $jsonFile.VersionInfo.ProductVersion
            Length = $jsonFile.Length
            LastWriteTime = $jsonFile.LastWriteTime
        })
    } catch {
        Write-Lines "ERROR reading System.Text.Json assembly identity: $($_.Exception.GetType().FullName): $($_.Exception.Message)"
    }
} else {
    Write-Lines 'ERROR: System.Text.Json.dll is not present at the package root.'
}

$depsPath = Join-Path $installRoot 'Sentinel.App.deps.json'
if (Test-Path -LiteralPath $depsPath -PathType Leaf) {
    try {
        $depsText = Get-Content -LiteralPath $depsPath -Raw
        $depsMatches = [regex]::Matches($depsText, '.{0,180}System\.Text\.Json.{0,260}', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($depsMatches.Count -eq 0) {
            Write-Lines 'ERROR: Sentinel.App.deps.json contains no System.Text.Json registration.'
        } else {
            foreach ($match in $depsMatches) {
                Write-Lines $match.Value
            }
        }
    } catch {
        Write-Lines "ERROR reading Sentinel.App.deps.json: $($_.Exception.GetType().FullName): $($_.Exception.Message)"
    }
} else {
    Write-Lines "ERROR: Sentinel.App.deps.json is missing at $depsPath"
}

Write-Section 'Registered application identity'
$manifestPath = Join-Path $installRoot 'AppxManifest.xml'
if (Test-Path -LiteralPath $manifestPath) {
    try {
        [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
        $app = $manifest.Package.Applications.Application | Select-Object -First 1
        Write-Lines ([pscustomobject]@{
            Id = $app.Id
            Executable = $app.Executable
            EntryPoint = $app.EntryPoint
            PackageIdentity = $manifest.Package.Identity.Name
            Publisher = $manifest.Package.Identity.Publisher
            Version = $manifest.Package.Identity.Version
            ProcessorArchitecture = $manifest.Package.Identity.ProcessorArchitecture
        })
    } catch {
        Write-Lines "Manifest parse failed: $($_.Exception.Message)"
    }
} else {
    Write-Lines "AppxManifest.xml not found at $manifestPath"
}

$since = (Get-Date).AddMinutes(-[Math]::Abs($LookbackMinutes))

if (-not $SkipLaunch) {
    Write-Section 'Fresh activation attempt'
    $aumid = "$($installed.PackageFamilyName)!App"
    Write-Lines "AUMID: $aumid"
    try {
        Get-Process -Name 'Sentinel.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        Start-Process -FilePath 'explorer.exe' -ArgumentList "shell:AppsFolder\$aumid"
        Start-Sleep -Seconds 8
        $running = @(Get-Process -Name 'Sentinel.App' -ErrorAction SilentlyContinue)
        if ($running.Count -gt 0) {
            $runningIds = @($running | ForEach-Object { $_.Id })
            Write-Lines "PASS: Sentinel.App remained running. PID(s): $($runningIds -join ', ')"
        } else {
            Write-Lines 'FAIL: Sentinel.App did not remain running after packaged activation.'
        }
    } catch {
        Write-Lines "Activation command failed: $($_.Exception.GetType().FullName): $($_.Exception.Message)"
    }
}

Write-Section 'Sentinel diagnostic files'
$candidateRoots = @(
    (Join-Path $env:LOCALAPPDATA 'Modern Methods\Sentinel AI\Logs'),
    (Join-Path $env:LOCALAPPDATA "Packages\$($installed.PackageFamilyName)")
)
foreach ($root in $candidateRoots) {
    Write-Lines "Root: $root"
    if (-not (Test-Path -LiteralPath $root)) {
        Write-Lines '  (not present)'
        continue
    }
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @('last-crash.txt','sentinel.log','sentinel.previous.log','bootstrap-launch.log') } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 8)
    if ($files.Count -eq 0) { Write-Lines '  (no Sentinel diagnostic files found)' }
    foreach ($file in $files) {
        Write-Lines "--- $($file.FullName) ---"
        Write-Lines (Get-Content -LiteralPath $file.FullName -Tail 160 -ErrorAction SilentlyContinue)
    }
}

Write-Section 'Application crash/runtime events'
$applicationEvents = Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=$since } -ErrorAction SilentlyContinue |
    Where-Object {
        $_.ProviderName -in @('.NET Runtime','Application Error','Windows Error Reporting','Application Hang') -or
        $_.Message -match 'Sentinel\.App|ModernMethods\.SentinelAI'
    } |
    Sort-Object TimeCreated -Descending |
    Select-Object -First 30 TimeCreated, ProviderName, Id, LevelDisplayName, Message
Write-Lines $applicationEvents

Write-Section 'AppModel-Runtime events'
$appModelEvents = Get-WinEvent -FilterHashtable @{ LogName='Microsoft-Windows-AppModel-Runtime/Admin'; StartTime=$since } -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'Sentinel|ModernMethods\.SentinelAI|!App' } |
    Sort-Object TimeCreated -Descending |
    Select-Object -First 30 TimeCreated, Id, LevelDisplayName, Message
Write-Lines $appModelEvents

Write-Section 'TWinUI activation events'
$twinUiEvents = Get-WinEvent -FilterHashtable @{ LogName='Microsoft-Windows-TWinUI/Operational'; StartTime=$since } -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'Sentinel|ModernMethods\.SentinelAI|!App' } |
    Sort-Object TimeCreated -Descending |
    Select-Object -First 30 TimeCreated, Id, LevelDisplayName, Message
Write-Lines $twinUiEvents

Write-Section 'Package deployment events'
$deploymentEvents = Get-WinEvent -FilterHashtable @{ LogName='Microsoft-Windows-AppXDeploymentServer/Operational'; StartTime=$since } -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'Sentinel|ModernMethods\.SentinelAI' } |
    Sort-Object TimeCreated -Descending |
    Select-Object -First 30 TimeCreated, Id, LevelDisplayName, Message
Write-Lines $deploymentEvents

Write-Section 'Summary'
$processNow = @(Get-Process -Name 'Sentinel.App' -ErrorAction SilentlyContinue)
$processIds = @($processNow | ForEach-Object { $_.Id })
Write-Lines ([pscustomobject]@{
    Package = $installed.PackageFullName
    InstallLocation = $installRoot
    ExePresent = (Test-Path -LiteralPath $exePath -PathType Leaf)
    ProcessRunning = ($processNow.Count -gt 0)
    ProcessIds = ($processIds -join ', ')
    OutputFile = $outputPath
})

Write-Host ''
Write-Host "Diagnostics saved to: $outputPath"
Write-Host 'Paste the System.Text.Json assembly identity/deps section plus the Summary and any ERROR/FAIL entries into the Sentinel investigation chat.'
