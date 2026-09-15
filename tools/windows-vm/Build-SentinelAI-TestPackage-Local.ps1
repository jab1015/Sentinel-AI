param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot

$branch = (& git branch --show-current).Trim()
if ($branch -ne 'feature/premium-privacy-foundation') {
    throw "Expected branch feature/premium-privacy-foundation, found '$branch'."
}

$sourceSha = (& git rev-parse HEAD).Trim()
if ([string]::IsNullOrWhiteSpace($sourceSha)) { throw 'Could not resolve current Git commit SHA.' }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'vswhere.exe was not found. Install Visual Studio Build Tools / Visual Studio with MSBuild.' }

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($msbuild) -or -not (Test-Path -LiteralPath $msbuild)) { throw 'MSBuild.exe was not found.' }

$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
if (-not (Test-Path -LiteralPath $kitsRoot)) { throw 'Windows 10/11 SDK tools folder was not found.' }

$signTool = $null
$makeAppx = $null
foreach ($sdkDir in (Get-ChildItem -LiteralPath $kitsRoot -Directory | Sort-Object Name -Descending)) {
    $candidateSign = Join-Path $sdkDir.FullName 'x64\signtool.exe'
    $candidateMake = Join-Path $sdkDir.FullName 'x64\makeappx.exe'
    if ((Test-Path -LiteralPath $candidateSign) -and (Test-Path -LiteralPath $candidateMake)) {
        $signTool = $candidateSign
        $makeAppx = $candidateMake
        break
    }
}

if (-not $signTool -or -not $makeAppx) { throw 'SignTool.exe and/or MakeAppx.exe were not found. Install the Windows SDK signing tools.' }

$outputDir = Join-Path $repoRoot 'artifacts\windows-vm-test'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "Repository: $repoRoot"
Write-Host "Branch: $branch"
Write-Host "Source SHA: $sourceSha"
Write-Host "MSBuild: $msbuild"
Write-Host "SignTool: $signTool"
Write-Host "MakeAppx: $makeAppx"
Write-Host "Output: $outputDir"
Write-Host ''
Write-Host 'Building subscription-free LocalDev x64 VM test MSIX...'
Write-Host 'Existing Visual Studio AppPackages folders will be preserved.'

$previousRunnerTemp = $env:RUNNER_TEMP
$localRunnerTemp = Join-Path ([IO.Path]::GetTempPath()) ("SentinelAI-local-runner-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $localRunnerTemp -Force | Out-Null
$env:RUNNER_TEMP = $localRunnerTemp

$baseScript = Join-Path $PSScriptRoot 'Build-SentinelAI-TestPackage.ps1'
$compatScript = Join-Path $localRunnerTemp 'Build-SentinelAI-TestPackage-Compat.ps1'
$scriptText = Get-Content -LiteralPath $baseScript -Raw

# The CI script's AppPackages cleanup is safe on an ephemeral GitHub runner but not on a
# developer workstation. Redirect local package-generation output into RUNNER_TEMP so old
# versioned Visual Studio AppPackages folders are never deleted by this wrapper.
$oldAppPackages = '$appPackages = ''src\SentinelAI\Sentinel.App\Sentinel.App (Package)\AppPackages'''
$newAppPackages = '$appPackages = Join-Path $env:RUNNER_TEMP ''SentinelAI-AppPackages'''
if (-not $scriptText.Contains($oldAppPackages)) { throw 'Expected AppPackages assignment was not found in package script.' }
$scriptText = $scriptText.Replace($oldAppPackages, $newAppPackages)

$oldMsBuild = '& $MsBuild $packageProject /restore /m /p:Configuration=$configuration /p:Platform=x64 /p:AppxBundle=Never /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxPackageSigningEnabled=false /fl "/flp:logfile=windows-vm-test-package.log;verbosity=diagnostic"'
$newMsBuild = '& $MsBuild $packageProject /restore /m /p:Configuration=$configuration /p:Platform=x64 /p:AppxBundle=Never /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxPackageSigningEnabled=false "/p:AppxPackageDir=$appPackages\" /fl "/flp:logfile=windows-vm-test-package.log;verbosity=diagnostic"'
if (-not $scriptText.Contains($oldMsBuild)) { throw 'Expected MSBuild package command was not found in package script.' }
$scriptText = $scriptText.Replace($oldMsBuild, $newMsBuild)

$oldRng = '[Security.Cryptography.RandomNumberGenerator]::GetBytes(48)'
$newRng = '$( $bytes = New-Object byte[] 48; $rng = [Security.Cryptography.RandomNumberGenerator]::Create(); try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }; $bytes )'
if (-not $scriptText.Contains($oldRng)) { throw 'Expected RNG expression was not found in package script.' }
$scriptText = $scriptText.Replace($oldRng, $newRng)

$oldArgumentList = '    foreach ($argument in $Arguments) { [void]$psi.ArgumentList.Add($argument) }'
$newArguments = '    $psi.Arguments = (($Arguments | ForEach-Object { ''"{0}"'' -f $_ }) -join '' '')'
if (-not $scriptText.Contains($oldArgumentList)) { throw 'Expected ProcessStartInfo.ArgumentList expression was not found in package script.' }
$scriptText = $scriptText.Replace($oldArgumentList, $newArguments)

Set-Content -LiteralPath $compatScript -Value $scriptText -Encoding UTF8

try {
    & $compatScript `
        -MsBuild $msbuild `
        -SignTool $signTool `
        -MakeAppx $makeAppx `
        -SourceSha $sourceSha `
        -OutputDir $outputDir

    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    $env:RUNNER_TEMP = $previousRunnerTemp
    if (Test-Path -LiteralPath $localRunnerTemp) {
        Remove-Item -LiteralPath $localRunnerTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host 'LOCAL VM PACKAGE BUILD COMPLETE.'
Write-Host "Artifacts: $outputDir"
Get-ChildItem -LiteralPath $outputDir -File | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
