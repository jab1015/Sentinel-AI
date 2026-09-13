[CmdletBinding()]
param(
    [string]$PackagePath = (Join-Path $PSScriptRoot 'SentinelAI-WindowsVM-x64.msix'),
    [string]$CertificatePath = (Join-Path $PSScriptRoot 'SentinelAI-TestSigning.cer'),
    [string]$HashFilePath = (Join-Path $PSScriptRoot 'SHA256SUMS.txt')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This installer must be run from PowerShell opened with Run as administrator.'
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

function Prompt-ForReboot {
    Write-Host ''
    Write-Warning 'REBOOT REQUIRED BEFORE EXPLORER TESTING.'
    Write-Host 'Sentinel AI File Explorer context-menu commands may not appear until Windows has restarted.'
    Write-Host ''

    while ($true) {
        $choice = (Read-Host 'Restart the VM now? Enter Y to reboot now or N to reboot later').Trim()
        if ($choice -match '^(?i)y(es)?$') {
            Write-Host 'Restarting Windows now...'
            Restart-Computer -Force
            return
        }
        if ($choice -match '^(?i)n(o)?$') {
            Write-Host ''
            Write-Warning 'REBOOT PENDING: Restart Windows before testing Sentinel AI Explorer context-menu commands.'
            return
        }
        Write-Host 'Please enter Y or N.'
    }
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

$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
$expectedPublisher = 'CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A'
if ($certificate.Subject -ne $expectedPublisher) {
    throw "Unexpected test-certificate subject '$($certificate.Subject)'. Expected '$expectedPublisher'."
}
if ($certificate.HasPrivateKey) {
    throw 'The VM handoff certificate unexpectedly contains a private key. Refusing to continue.'
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
if (-not (Test-Path $trustedPeoplePath)) {
    Write-Host 'Installing the Sentinel AI VM test certificate into Local Computer > Trusted People...'
    Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
}
else {
    Write-Host 'The Sentinel AI VM test certificate is already trusted in Local Computer > Trusted People.'
}

$signature = Get-AuthenticodeSignature -LiteralPath $PackagePath
if ($signature.Status -ne 'Valid') {
    throw "MSIX signature validation failed after trusting the test certificate. Status: $($signature.Status). Message: $($signature.StatusMessage)"
}
if ($null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
    throw 'MSIX signer certificate does not match SentinelAI-TestSigning.cer.'
}
Write-Host "MSIX signature: Valid ($($signature.SignerCertificate.Thumbprint))"

$existing = @(Get-AppxPackage -Name 'ModernMethods.SentinelAI' -ErrorAction SilentlyContinue)
if ($existing.Count -gt 0) {
    Write-Host 'An existing ModernMethods.SentinelAI package is installed for this user:'
    $existing | ForEach-Object { Write-Host "  $($_.PackageFullName)" }
    Write-Host 'Add-AppxPackage will perform the normal package deployment/version checks; no existing package is removed automatically.'
}

Write-Host 'Installing signed Sentinel AI VM test MSIX...'
Add-AppxPackage -Path $PackagePath -ForceApplicationShutdown -ErrorAction Stop

$installed = Get-AppxPackage -Name 'ModernMethods.SentinelAI' -ErrorAction Stop | Sort-Object Version -Descending | Select-Object -First 1
if ($null -eq $installed) {
    throw 'Add-AppxPackage returned without error, but ModernMethods.SentinelAI is not registered for the current user.'
}

Write-Host ''
Write-Host 'SUCCESS: Sentinel AI VM test package is installed.'
Write-Host "PackageFullName: $($installed.PackageFullName)"
Write-Host "Version:         $($installed.Version)"
Write-Host "Architecture:    $($installed.Architecture)"
Write-Host 'The test certificate remains in Local Computer > Trusted People for this VM test package.'

$launched = $false
try {
    $appUserModelId = "$($installed.PackageFamilyName)!App"
    Write-Host ''
    Write-Host 'Launching Sentinel AI so first-run setup can complete...'
    Start-Process -FilePath 'explorer.exe' -ArgumentList "shell:AppsFolder\$appUserModelId"
    Start-Sleep -Seconds 2
    $launched = @(Get-Process -Name 'Sentinel.App' -ErrorAction SilentlyContinue).Count -gt 0
}
catch {
    Write-Warning "Sentinel AI was installed but could not be launched automatically: $($_.Exception.Message)"
}

if ($launched) {
    Write-Host 'Sentinel AI launched. Follow the Restart Windows prompt shown by Sentinel.'
    Write-Warning 'Explorer right-click testing is not valid until Windows has restarted.'
}
else {
    Write-Warning 'Sentinel AI did not confirm an automatic launch. Open Sentinel AI manually, then restart Windows before Explorer testing.'
    Prompt-ForReboot
}
