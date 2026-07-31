param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot ".."))
)

$ErrorActionPreference = "Stop"

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$solution = Join-Path $RepositoryRoot "src/SentinelAI/Sentinel.App/Sentinel.App.sln"
$appProject = Join-Path $RepositoryRoot "src/SentinelAI/Sentinel.App/Sentinel.App/Sentinel.App.csproj"
$packageProject = Join-Path $RepositoryRoot "src/SentinelAI/Sentinel.App/Sentinel.App (Package)/Sentinel.App (Package).wapproj"
$packageManifest = Join-Path $RepositoryRoot "src/SentinelAI/Sentinel.App/Sentinel.App (Package)/Package.appxmanifest"
$nativeMethods = Join-Path $RepositoryRoot "src/SentinelAI/Sentinel.App/Sentinel.App/NativeMethods.txt"

$requiredFiles = @($solution, $appProject, $packageProject, $packageManifest, $nativeMethods)
foreach ($file in $requiredFiles) {
    Assert-Condition (Test-Path -LiteralPath $file) "Required release file is missing: $file"
}

[xml]$appXml = Get-Content -LiteralPath $appProject -Raw
[xml]$packageXml = Get-Content -LiteralPath $packageProject -Raw

$appTargetFramework = [string]$appXml.Project.PropertyGroup.TargetFramework | Where-Object { $_ }
$appMinVersion = [string]$appXml.Project.PropertyGroup.TargetPlatformMinVersion | Where-Object { $_ }
$appPlatforms = [string]$appXml.Project.PropertyGroup.Platforms | Where-Object { $_ }

$packageNamespace = New-Object System.Xml.XmlNamespaceManager($packageXml.NameTable)
$packageNamespace.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
$packageTargetVersion = $packageXml.SelectSingleNode("//msb:TargetPlatformVersion", $packageNamespace).InnerText
$packageMinVersion = $packageXml.SelectSingleNode("//msb:TargetPlatformMinVersion", $packageNamespace).InnerText
$entryPointProject = $packageXml.SelectSingleNode("//msb:EntryPointProjectUniqueName", $packageNamespace).InnerText

Assert-Condition ($appTargetFramework -match "^net8\.0-windows10\.0\.19041\.0$") "Unexpected app target framework: $appTargetFramework"
Assert-Condition ($appMinVersion -eq "10.0.17763.0") "Unexpected app minimum Windows version: $appMinVersion"
Assert-Condition ($packageTargetVersion -eq "10.0.19041.0") "Package target Windows version is not aligned with the app project."
Assert-Condition ($packageMinVersion -eq $appMinVersion) "Package minimum Windows version is not aligned with the app project."
Assert-Condition ($appPlatforms -eq "x86;x64;ARM64") "Expected x86, x64, and ARM64 release platforms."
Assert-Condition ($entryPointProject -eq "..\Sentinel.App\Sentinel.App.csproj") "Packaging project does not reference the expected application project."

$requiredPackages = @(
    "Microsoft.Windows.CsWin32",
    "Microsoft.Windows.SDK.BuildTools",
    "Microsoft.WindowsAppSDK",
    "System.Diagnostics.EventLog",
    "System.ServiceProcess.ServiceController"
)

$appPackageNames = @($appXml.Project.ItemGroup.PackageReference | ForEach-Object { $_.Include })
foreach ($package in $requiredPackages) {
    Assert-Condition ($appPackageNames -contains $package) "Required package reference is missing: $package"
}

$nativeApiNames = Get-Content -LiteralPath $nativeMethods | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
Assert-Condition ($nativeApiNames -contains "GetSystemTimes") "NativeMethods.txt is missing GetSystemTimes."
Assert-Condition ($nativeApiNames -contains "GlobalMemoryStatusEx") "NativeMethods.txt is missing GlobalMemoryStatusEx."

Write-Host "Sentinel AI release configuration verification passed."
Write-Host "Target framework: $appTargetFramework"
Write-Host "Minimum Windows version: $appMinVersion"
Write-Host "Platforms: $appPlatforms"
Write-Host "Packaging target: $packageTargetVersion"
Write-Host "Signing remains disabled until the dedicated code-signing release step."
