param(
    [string]$PackageName = "ModernMethods.SentinelAI"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Sentinel AI DPI Awareness Verification ==="

$pkg = Get-AppxPackage | Where-Object { $_.Name -eq $PackageName } | Sort-Object Version -Descending | Select-Object -First 1
if (-not $pkg) {
    throw "Sentinel AI package '$PackageName' is not installed."
}

$exe = Join-Path $pkg.InstallLocation "Sentinel.App.exe"
if (-not (Test-Path $exe)) {
    throw "Sentinel.App.exe was not found at $exe"
}

Write-Host "Package: $($pkg.Name) $($pkg.Version)"
Write-Host "Executable: $exe"

$mt = Get-Command mt.exe -ErrorAction SilentlyContinue
if (-not $mt) {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $candidate = Get-ChildItem $kitsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\mt.exe" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if ($candidate) { $mt = Get-Item $candidate }
}

if (-not $mt) {
    throw "mt.exe was not found. Install the Windows SDK or run from a Developer PowerShell."
}

$temp = Join-Path $env:TEMP "SentinelAI.embedded.manifest.xml"
& $mt.Source "-inputresource:$exe;#1" "-out:$temp" | Out-Null
if (-not (Test-Path $temp)) {
    throw "Could not extract the embedded manifest from Sentinel.App.exe."
}

$manifest = Get-Content $temp -Raw
$legacy = $manifest -match '<dpiAware[^>]*>\s*true/pm\s*</dpiAware>'
$modern = $manifest -match '<dpiAwareness[^>]*>\s*PerMonitorV2(?:,PerMonitor)?\s*</dpiAwareness>'

Write-Host "Legacy dpiAware=true/pm: $legacy"
Write-Host "Modern dpiAwareness=PerMonitorV2: $modern"

if ($legacy -and $modern) {
    Write-Host "PASS: Sentinel.App.exe contains both required DPI-awareness declarations."
    Write-Host "The WACK 'Failed to process the binary / not DPI Aware' result can be treated as a certification-tool warning rather than a missing Sentinel manifest declaration."
    exit 0
}

Write-Host "FAIL: One or more DPI-awareness declarations are missing from the installed executable manifest."
Write-Host "Extracted manifest: $temp"
exit 1
