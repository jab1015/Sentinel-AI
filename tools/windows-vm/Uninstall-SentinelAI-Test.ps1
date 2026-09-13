[CmdletBinding()]
param(
    [switch]$RemoveTestCertificate,
    [string]$CertificatePath = (Join-Path $PSScriptRoot 'SentinelAI-TestSigning.cer')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This uninstaller must be run from PowerShell opened with Run as administrator.'
    }
}

Assert-Administrator

$packages = @(Get-AppxPackage -Name 'ModernMethods.SentinelAI' -ErrorAction SilentlyContinue)
if ($packages.Count -eq 0) {
    Write-Host 'No ModernMethods.SentinelAI package is registered for the current user.'
}
else {
    foreach ($package in $packages) {
        Write-Host "Removing Sentinel AI package: $($package.PackageFullName)"
        Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
    }
    Write-Host 'Sentinel AI test package removal completed.'
}

if ($RemoveTestCertificate) {
    $CertificatePath = [IO.Path]::GetFullPath($CertificatePath)
    if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
        throw "Cannot safely identify the test certificate because the public certificate file is missing: $CertificatePath"
    }

    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
    $expectedPublisher = 'CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A'
    if ($certificate.Subject -ne $expectedPublisher) {
        throw "Refusing certificate removal: unexpected subject '$($certificate.Subject)'."
    }

    $trustedPeoplePath = "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
    if (Test-Path $trustedPeoplePath) {
        Write-Host "Removing only Sentinel AI VM test certificate $($certificate.Thumbprint) from Local Computer > Trusted People."
        Remove-Item -LiteralPath $trustedPeoplePath -Force
    }
    else {
        Write-Host 'The supplied Sentinel AI VM test certificate is not present in Local Computer > Trusted People.'
    }
}
else {
    Write-Host 'The VM test certificate was left installed. Re-run with -RemoveTestCertificate to remove only the certificate matching SentinelAI-TestSigning.cer.'
}
