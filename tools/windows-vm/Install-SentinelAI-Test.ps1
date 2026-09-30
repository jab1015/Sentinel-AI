[CmdletBinding()]
param(
    [string]$PackagePath = (Join-Path $PSScriptRoot 'SentinelAI-WindowsVM-x64.msix'),
    [string]$CertificatePath = (Join-Path $PSScriptRoot 'SentinelAI-TestSigning.cer'),
    [string]$HashFilePath = (Join-Path $PSScriptRoot 'SHA256SUMS.txt')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Set-Location $PSScriptRoot

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This installer must run with Administrator permission. Use Install-SentinelAI-Test.cmd.'
    }
}

function Get-ExpectedPackageHash {
    param([string]$HashFile, [string]$PackageFileName)

    if (-not (Test-Path -LiteralPath $HashFile -PathType Leaf)) {
        return $null
    }

    foreach ($line in Get-Content -LiteralPath $HashFile) {
        if ($line -match '^([0-9A-Fa-f]{64})\s+\*?(.+)$') {
            if ([IO.Path]::GetFileName($Matches[2].Trim()) -ieq $PackageFileName) {
                return $Matches[1].ToUpperInvariant()
            }
        }
    }

    throw "SHA256SUMS.txt does not contain an entry for $PackageFileName."
}

function Stop-ExistingSentinelProcesses {
    $processes = @(Get-Process -Name 'Sentinel.App' -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) {
        return
    }

    Write-Host "Closing $($processes.Count) existing Sentinel.App process(es) before package deployment..."
    foreach ($process in $processes) {
        try {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Could not stop Sentinel.App PID $($process.Id): $($_.Exception.Message)"
        }
    }

    $deadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 250
        $remaining = @(Get-Process -Name 'Sentinel.App' -ErrorAction SilentlyContinue)
    } while ($remaining.Count -gt 0 -and (Get-Date) -lt $deadline)

    if ($remaining.Count -gt 0) {
        throw "An older Sentinel.App process is still running (PID(s): $($remaining.Id -join ', ')). Close it in Task Manager and run this installer again."
    }
}

function Get-VisibleSentinelWindows {
    $processes = @(Get-Process -Name 'Sentinel.App' -ErrorAction SilentlyContinue)
    return @(
        $processes |
            Where-Object {
                $_.MainWindowHandle -ne [IntPtr]::Zero -and
                $_.MainWindowTitle -like 'Sentinel AI*'
            }
    )
}

function Invoke-LaunchDiagnostics {
    $collector = Join-Path $PSScriptRoot 'Collect-SentinelAI-LaunchDiagnostics.ps1'
    if (-not (Test-Path -LiteralPath $collector -PathType Leaf)) {
        Write-Warning 'Collect-SentinelAI-LaunchDiagnostics.ps1 is missing from this package.'
        return $null
    }

    Write-Host ''
    Write-Host 'Collecting Sentinel launch diagnostics...'
    try {
        & $collector -LookbackMinutes 20 -SkipLaunch
    }
    catch {
        Write-Warning "Diagnostic collection encountered an error: $($_.Exception.Message)"
    }

    $latest = Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'SentinelAI-LaunchDiagnostics-*.txt' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        return $null
    }

    return $latest.FullName
}

Assert-Administrator

$PackagePath = [IO.Path]::GetFullPath($PackagePath)
$CertificatePath = [IO.Path]::GetFullPath($CertificatePath)
$HashFilePath = [IO.Path]::GetFullPath($HashFilePath)

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "MSIX not found: $PackagePath"
}
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
    throw "Public test certificate not found: $CertificatePath"
}

Write-Host 'Sentinel AI Windows VM test installer'
Write-Host "Package:     $PackagePath"
Write-Host "Certificate: $CertificatePath"

$actualHash = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToUpperInvariant()
Write-Host "SHA-256:     $actualHash"

$expectedHash = Get-ExpectedPackageHash -HashFile $HashFilePath -PackageFileName ([IO.Path]::GetFileName($PackagePath))
if ($null -ne $expectedHash) {
    if ($actualHash -ne $expectedHash) {
        throw "Package SHA-256 mismatch. Expected $expectedHash but found $actualHash."
    }
    Write-Host 'Package hash matches SHA256SUMS.txt.'
}

$certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2($CertificatePath)
try {
    $expectedPublisher = 'CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A'
    if ($certificate.Subject -ne $expectedPublisher) {
        throw "Unexpected test-certificate subject '$($certificate.Subject)'. Expected '$expectedPublisher'."
    }
    if ($certificate.HasPrivateKey) {
        throw 'The public VM test certificate unexpectedly contains a private key.'
    }

    $codeSigningOid = '1.3.6.1.5.5.7.3.3'
    $hasCodeSigningEku = $false
    foreach ($extension in $certificate.Extensions) {
        if ($extension -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
            foreach ($oid in $extension.EnhancedKeyUsages) {
                if ($oid.Value -eq $codeSigningOid) {
                    $hasCodeSigningEku = $true
                }
            }
        }
    }
    if (-not $hasCodeSigningEku) {
        throw 'The supplied certificate is not marked for Code Signing.'
    }

    $trustedPeoplePath = "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
    if (-not (Test-Path -LiteralPath $trustedPeoplePath)) {
        Write-Host 'Installing the Sentinel AI VM test certificate into Local Computer > Trusted People...'
        Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    }
    else {
        Write-Host 'The Sentinel AI VM test certificate is already trusted.'
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $PackagePath
    if ($signature.Status -ne 'Valid') {
        throw "MSIX signature validation failed. Status: $($signature.Status). Message: $($signature.StatusMessage)"
    }
    if ($null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw 'MSIX signer certificate does not match SentinelAI-TestSigning.cer.'
    }

    Write-Host "MSIX signature: Valid ($($signature.SignerCertificate.Thumbprint))"

    Stop-ExistingSentinelProcesses

    $existing = @(Get-AppxPackage -Name 'ModernMethods.SentinelAI' -ErrorAction SilentlyContinue | Sort-Object Version -Descending)
    if ($existing.Count -gt 0) {
        Write-Host "Existing Sentinel package: $($existing[0].PackageFullName)"
    }

    Write-Host 'Installing/updating Sentinel AI...'
    Add-AppxPackage -Path $PackagePath -ForceApplicationShutdown -ErrorAction Stop

    $installed = Get-AppxPackage -Name 'ModernMethods.SentinelAI' -ErrorAction Stop |
        Sort-Object Version -Descending |
        Select-Object -First 1

    if ($null -eq $installed) {
        throw 'Windows completed package deployment, but Sentinel AI is not registered for the current user.'
    }

    Write-Host ''
    Write-Host 'Package installation completed.'
    Write-Host "PackageFullName: $($installed.PackageFullName)"
    Write-Host "Version:         $($installed.Version)"
    Write-Host "Architecture:    $($installed.Architecture)"

    Stop-ExistingSentinelProcesses

    $appUserModelId = "$($installed.PackageFamilyName)!App"
    Write-Host ''
    Write-Host "Launching $appUserModelId..."
    Start-Process -FilePath 'explorer.exe' -ArgumentList "shell:AppsFolder\$appUserModelId"

    $deadline = (Get-Date).AddSeconds(20)
    $visibleWindows = @()
    do {
        Start-Sleep -Milliseconds 500
        $visibleWindows = @(Get-VisibleSentinelWindows)
    } while ($visibleWindows.Count -eq 0 -and (Get-Date) -lt $deadline)

    if ($visibleWindows.Count -eq 0) {
        $running = @(Get-Process -Name 'Sentinel.App' -ErrorAction SilentlyContinue)
        if ($running.Count -gt 0) {
            Write-Warning "Sentinel.App is running but no visible Sentinel AI main window appeared."
            foreach ($process in $running) {
                Write-Host "  PID $($process.Id) | Handle=$($process.MainWindowHandle) | Title='$($process.MainWindowTitle)'"
            }
        }
        else {
            Write-Warning 'Sentinel.App did not remain running after launch.'
        }

        $diagnosticPath = Invoke-LaunchDiagnostics
        if ($null -ne $diagnosticPath) {
            throw "Sentinel installed but did not open a visible main window. Diagnostics: $diagnosticPath"
        }

        throw 'Sentinel installed but did not open a visible main window.'
    }

    Write-Host ''
    Write-Host "SUCCESS: Sentinel AI opened a visible main window (PID(s): $($visibleWindows.Id -join ', '))."
    Write-Host 'Restart Windows before testing the File Explorer context-menu integration.'
    Write-Host 'Installation is complete. This window will close automatically.'
}
finally {
    $certificate.Dispose()
}

exit 0
