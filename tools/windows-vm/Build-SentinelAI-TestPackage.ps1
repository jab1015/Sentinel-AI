param(
    [Parameter(Mandatory = $true)][string]$MsBuild,
    [Parameter(Mandatory = $true)][string]$SignTool,
    [Parameter(Mandatory = $true)][string]$MakeAppx,
    [Parameter(Mandatory = $true)][string]$SourceSha,
    [string]$OutputDir = 'artifacts/windows-vm-test',
    [string]$PackageName = 'SentinelAI-WindowsVM-x64.msix',
    [string]$CertName = 'SentinelAI-TestSigning.cer'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$manifestPath = 'src\SentinelAI\Sentinel.App\Sentinel.App (Package)\Package.appxmanifest'
$packageProject = 'src\SentinelAI\Sentinel.App\Sentinel.App (Package)\Sentinel.App (Package).wapproj'
$appPackages = 'src\SentinelAI\Sentinel.App\Sentinel.App (Package)\AppPackages'
$appProject = 'src\SentinelAI\Sentinel.App\Sentinel.App\Sentinel.App.csproj'
$entitlementSource = 'src\SentinelAI\Sentinel.App\Sentinel.App\Services\PremiumPrivacyEntitlementClient.cs'
$configuration = 'LocalDev'
$expectedName = 'ModernMethods.SentinelAI'
$expectedPublisher = 'CN=EA91DFAA-447F-4250-AC3D-047D8D7F831A'
$pfxPath = Join-Path $env:RUNNER_TEMP 'SentinelAI-Ephemeral-TestSigning.pfx'
$certObject = $null

function Invoke-BoundedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][int]$TimeoutMilliseconds,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FilePath
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$psi.ArgumentList.Add($argument) }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    try {
        if (-not $process.Start()) { throw "Failed to start $Description." }

        # Drain redirected output while the child runs so a full pipe cannot deadlock it.
        # The drain itself is also bounded after process exit because a descendant can
        # otherwise retain an inherited stdout/stderr handle indefinitely.
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            try { $process.Kill($true) } catch {}
            [void]$process.WaitForExit(10000)
            throw "$Description exceeded the $([int]($TimeoutMilliseconds / 1000))-second safety bound."
        }

        $drainTimer = [Diagnostics.Stopwatch]::StartNew()
        while ((-not $stdoutTask.IsCompleted -or -not $stderrTask.IsCompleted) -and $drainTimer.ElapsedMilliseconds -lt 15000) {
            Start-Sleep -Milliseconds 100
        }
        $drainTimer.Stop()
        if (-not $stdoutTask.IsCompleted -or -not $stderrTask.IsCompleted) {
            throw "$Description exited but redirected output did not close within 15 seconds. A descendant process likely retained an inherited pipe handle."
        }

        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if (-not [string]::IsNullOrEmpty($stdout)) {
            $stdout | Tee-Object -FilePath $LogPath -Append | Write-Host
        }
        if (-not [string]::IsNullOrEmpty($stderr)) {
            $stderr | Tee-Object -FilePath $LogPath -Append | Write-Host
        }
        if ($process.ExitCode -ne 0) { throw "$Description failed with exit code $($process.ExitCode)." }
    }
    finally {
        $process.Dispose()
    }
}

function Get-PeMachine {
    param([Parameter(Mandatory = $true)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a) { throw "Invalid PE file: $Path" }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($peOffset -lt 0 -or ($peOffset + 6) -gt $bytes.Length) { throw "Invalid PE header offset: $Path" }
    return [BitConverter]::ToUInt16($bytes, $peOffset + 4)
}

try {
    [xml]$sourceManifest = Get-Content -LiteralPath $manifestPath -Raw
    $identity = $sourceManifest.Package.Identity
    if ($identity.Name -ne $expectedName) { throw "Unexpected package name: $($identity.Name)" }
    if ($identity.Publisher -ne $expectedPublisher) { throw "Unexpected package publisher: $($identity.Publisher)" }
    Write-Host "Production package identity preserved: $($identity.Name), $($identity.Publisher), version $($identity.Version)."

    # This package is intentionally LocalDev-only. The compile symbol is not defined by
    # Release, so there is no runtime switch that can disable Store/gateway enforcement.
    $appProjectText = Get-Content -LiteralPath $appProject -Raw
    if (-not $appProjectText.Contains('<Configurations>Debug;Release;LocalDev</Configurations>') -or
        -not $appProjectText.Contains('SENTINEL_LOCAL_DEV')) {
        throw 'Sentinel.App LocalDev configuration does not define SENTINEL_LOCAL_DEV.'
    }

    $entitlementText = Get-Content -LiteralPath $entitlementSource -Raw
    if (-not $entitlementText.Contains('#if SENTINEL_LOCAL_DEV') -or
        -not $entitlementText.Contains('LocalVmTestAllowed') -or
        -not $entitlementText.Contains('#else') -or
        -not $entitlementText.Contains('v1/store/collections-ticket') -or
        -not $entitlementText.Contains('v1/privacy/capability/validate')) {
        throw 'Premium Privacy LocalDev/Release entitlement boundary is missing or incomplete.'
    }
    Write-Host 'Verified compile-time LocalDev-only Premium Privacy test entitlement boundary.'

    if (Test-Path $appPackages) { Remove-Item $appPackages -Recurse -Force }
    $msbuildArguments = @(
        $packageProject,
        '/restore',
        '/m',
        '/nr:false',
        "/p:Configuration=$configuration",
        '/p:Platform=x64',
        '/p:AppxBundle=Never',
        '/p:UapAppxPackageBuildMode=SideloadOnly',
        '/p:AppxPackageSigningEnabled=false',
        '/fl',
        '/flp:logfile=windows-vm-test-package.log;verbosity=diagnostic'
    )
    Invoke-BoundedProcess -FilePath $MsBuild -Arguments $msbuildArguments -TimeoutMilliseconds 900000 -LogPath 'windows-vm-test-msbuild-console.log' -Description 'MSBuild LocalDev x64 package build'

    $msixes = @(Get-ChildItem $appPackages -Recurse -File -Filter '*.msix' | Where-Object { $_.FullName -notmatch '[\\/]Dependencies[\\/]' })
    if ($msixes.Count -ne 1) { throw "Expected exactly one generated MSIX, found $($msixes.Count)." }

    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    $signedPackage = Join-Path $OutputDir $PackageName
    Copy-Item -LiteralPath $msixes[0].FullName -Destination $signedPackage -Force

    $rsa = [Security.Cryptography.RSA]::Create(3072)
    try {
        $dn = [Security.Cryptography.X509Certificates.X500DistinguishedName]::new($expectedPublisher)
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            $dn,
            $rsa,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $request.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $request.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
                [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,
                $true))
        $ekus = [Security.Cryptography.OidCollection]::new()
        [void]$ekus.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3', 'Code Signing'))
        $request.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($ekus, $true))

        $signingCert = $request.CreateSelfSigned(
            [DateTimeOffset]::UtcNow.AddMinutes(-5),
            [DateTimeOffset]::UtcNow.AddDays(30))
        try {
            if (-not $signingCert.HasPrivateKey) { throw 'Generated test certificate has no private key.' }
            if ($signingCert.Subject -ne $expectedPublisher) {
                throw "Certificate subject '$($signingCert.Subject)' does not match package Publisher."
            }

            $passwordPlain = [Convert]::ToBase64String(
                [Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
            Write-Output "::add-mask::$passwordPlain"
            [IO.File]::WriteAllBytes(
                $pfxPath,
                $signingCert.Export(
                    [Security.Cryptography.X509Certificates.X509ContentType]::Pfx,
                    $passwordPlain))
            $publicCertPath = Join-Path $OutputDir $CertName
            [IO.File]::WriteAllBytes(
                $publicCertPath,
                $signingCert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        }
        finally {
            $signingCert.Dispose()
        }
    }
    finally {
        $rsa.Dispose()
    }

    $certObject = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $OutputDir $CertName))
    if ($certObject.HasPrivateKey) { throw 'Exported public VM certificate unexpectedly contains a private key.' }
    $hasCodeSigningEku = $false
    foreach ($extension in $certObject.Extensions) {
        if ($extension -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
            foreach ($oid in $extension.EnhancedKeyUsages) {
                if ($oid.Value -eq '1.3.6.1.5.5.7.3.3') { $hasCodeSigningEku = $true }
            }
        }
    }
    if (-not $hasCodeSigningEku) { throw 'Generated public certificate is missing the Code Signing EKU.' }

    Invoke-BoundedProcess -FilePath $SignTool -Arguments @('sign','/f',$pfxPath,'/p',$passwordPlain,'/fd','SHA256','/v',$signedPackage) -TimeoutMilliseconds 120000 -LogPath 'windows-vm-test-signing.log' -Description 'SignTool sign'
    Remove-Item -LiteralPath $pfxPath -Force
    Write-Host 'MSIX signing completed; runner-temp private PFX deleted.'

    # The artifact intentionally carries only the public self-signed test certificate.
    # Final Windows signature/trust enforcement is performed by Add-AppxPackage in the
    # workflow after the certificate is placed in LocalMachine\TrustedPeople. That is
    # the same Windows package-deployment path used by the VM installer and fails closed
    # on tampering, signer mismatch, or an invalid package signature.
    Write-Host 'MSIX signed. Final signature/trust validation will be enforced by Windows package deployment.'

    $unpackRoot = Join-Path $env:RUNNER_TEMP 'sentinel-windows-vm-test-unpacked'
    if (Test-Path $unpackRoot) { Remove-Item $unpackRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $unpackRoot | Out-Null
    Invoke-BoundedProcess -FilePath $MakeAppx -Arguments @('unpack','/p',$signedPackage,'/d',$unpackRoot,'/o') -TimeoutMilliseconds 180000 -LogPath 'windows-vm-test-makeappx.log' -Description 'MakeAppx package unpack'

    $required = @('Sentinel.App.exe', 'Sentinel.PrivilegedBroker.exe', 'Sentinel.ExplorerExtension.dll')
    foreach ($name in $required) {
        $matches = @(Get-ChildItem $unpackRoot -Recurse -File -Filter $name)
        if ($matches.Count -ne 1) { throw "Expected exactly one $name in the package, found $($matches.Count)." }
        $machine = Get-PeMachine -Path $matches[0].FullName
        if ($machine -ne 0x8664) { throw "$name is not x64 PE (machine=0x$('{0:X4}' -f $machine))." }
        Write-Host "Validated x64 $name at $($matches[0].FullName.Substring($unpackRoot.Length + 1))."
    }

    [xml]$packagedManifest = Get-Content -LiteralPath (Join-Path $unpackRoot 'AppxManifest.xml') -Raw
    $packagedIdentity = $packagedManifest.Package.Identity
    if ($packagedIdentity.Name -ne $expectedName) { throw "Packaged identity name mismatch: $($packagedIdentity.Name)" }
    if ($packagedIdentity.Publisher -ne $expectedPublisher) { throw "Packaged Publisher mismatch: $($packagedIdentity.Publisher)" }
    if ($packagedIdentity.ProcessorArchitecture -ne 'x64') { throw "Expected x64 package architecture, found '$($packagedIdentity.ProcessorArchitecture)'." }

    $manifestText = Get-Content -LiteralPath (Join-Path $unpackRoot 'AppxManifest.xml') -Raw
    foreach ($fragment in @(
        'Category="windows.comServer"',
        'Category="windows.fileExplorerContextMenus"',
        'Sentinel.ExplorerExtension.dll',
        '6C5E88B7-2A44-4B6D-9A6C-4F1A5C9F6E21',
        'Type="*"',
        'Type="Directory"')) {
        if ($manifestText -notlike "*$fragment*") { throw "Packaged manifest is missing expected Explorer registration fragment: $fragment" }
    }

    Copy-Item -LiteralPath 'tools\windows-vm\Install-SentinelAI-Test.ps1' -Destination $OutputDir -Force
    Copy-Item -LiteralPath 'tools\windows-vm\Uninstall-SentinelAI-Test.ps1' -Destination $OutputDir -Force
    $hash = (Get-FileHash -LiteralPath $signedPackage -Algorithm SHA256).Hash
    "$hash  $PackageName" | Set-Content -LiteralPath (Join-Path $OutputDir 'SHA256SUMS.txt') -Encoding ascii
    @(
        'Branch=feature/premium-privacy-foundation',
        "SourceSHA=$SourceSha",
        'Architecture=x64',
        "Configuration=$configuration",
        'SubscriptionMode=LOCALDEV_VM_TEST_BYPASS_ONLY',
        'ReleaseStoreEntitlement=UNCHANGED_AND_REQUIRED',
        'Format=MSIX',
        "Package=$PackageName",
        "Certificate=$CertName",
        "CertificateThumbprint=$($certObject.Thumbprint)",
        "SHA256=$hash",
        'Signing=Ephemeral runner-only self-signed code-signing PFX; public CER only retained',
        'SignatureValidation=Windows Add-AppxPackage deployment after LocalMachine TrustedPeople trust, plus launch smoke test',
        'ManifestRegistration=PASS',
        'RequiredBinaries=PASS',
        'PEArchitecture=PASS',
        'ProductionStoreReady=NO',
        'MergeToMain=NO'
    ) | Set-Content -LiteralPath (Join-Path $OutputDir 'PACKAGE-METADATA.txt') -Encoding utf8

    $privateMaterial = @(Get-ChildItem $OutputDir -Recurse -File | Where-Object { $_.Extension -in @('.pfx', '.p12', '.key', '.pem') })
    if ($privateMaterial.Count -ne 0) { throw 'Private signing material was found in the artifact staging directory.' }

    Write-Host "Package SHA-256: $hash"
    Write-Host 'WINDOWS VM LOCALDEV TEST PACKAGE QUALIFICATION: PASS'
}
finally {
    if ($certObject) { $certObject.Dispose() }
    if (Test-Path -LiteralPath $pfxPath) {
        Remove-Item -LiteralPath $pfxPath -Force -ErrorAction SilentlyContinue
    }
}
