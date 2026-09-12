param(
    [switch]$RunElevatedProbe
)

$ErrorActionPreference = 'Stop'
$packageIdentityName = 'ModernMethods.SentinelAI'
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Evidence
    )

    $results.Add([pscustomobject]@{
        Check = $Name
        Result = if ($Passed) { 'PASS' } else { 'FAIL' }
        Evidence = $Evidence
    })
}

if (-not ('SentinelPackageIdentity.NativeMethods' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SentinelPackageIdentity
{
    public static class NativeMethods
    {
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetPackageFullName(IntPtr processHandle, ref uint packageFullNameLength, StringBuilder packageFullName);
    }
}
"@
}

function Get-ProcessPackageFullName {
    param([int]$ProcessId)

    $handle = [SentinelPackageIdentity.NativeMethods]::OpenProcess(
        [SentinelPackageIdentity.NativeMethods]::PROCESS_QUERY_LIMITED_INFORMATION,
        $false,
        $ProcessId)

    if ($handle -eq [IntPtr]::Zero) {
        throw "OpenProcess failed for PID $ProcessId (Win32=$([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
    }

    try {
        [uint32]$length = 0
        $first = [SentinelPackageIdentity.NativeMethods]::GetPackageFullName($handle, [ref]$length, $null)
        if ($first -ne [SentinelPackageIdentity.NativeMethods]::ERROR_INSUFFICIENT_BUFFER -or $length -le 1) {
            throw "GetPackageFullName size query failed for PID $ProcessId (result=$first, length=$length)."
        }

        $builder = [Text.StringBuilder]::new([int]$length)
        $second = [SentinelPackageIdentity.NativeMethods]::GetPackageFullName($handle, [ref]$length, $builder)
        if ($second -ne 0) {
            throw "GetPackageFullName failed for PID $ProcessId (result=$second)."
        }

        return $builder.ToString()
    }
    finally {
        [void][SentinelPackageIdentity.NativeMethods]::CloseHandle($handle)
    }
}

Write-Host '=== Sentinel AI Privileged Broker Runtime Validation ==='
Write-Host "Elevated probe requested: $RunElevatedProbe"
Write-Host ''

$package = Get-AppxPackage -Name $packageIdentityName -ErrorAction SilentlyContinue | Select-Object -First 1
Add-Result 'Sentinel package installed' ($null -ne $package) $(
    if ($package) { "PackageFullName=$($package.PackageFullName); InstallLocation=$($package.InstallLocation)" }
    else { "Package '$packageIdentityName' is not installed for the current user." }
)

if (-not $package) {
    $results | Format-Table -AutoSize -Wrap
    exit 1
}

$appPath = Join-Path $package.InstallLocation 'Sentinel.App.exe'
$brokerPath = Join-Path $package.InstallLocation 'Sentinel.PrivilegedBroker.exe'
Add-Result 'Packaged Sentinel.App.exe present' (Test-Path -LiteralPath $appPath -PathType Leaf) $appPath
Add-Result 'Packaged privileged broker present' (Test-Path -LiteralPath $brokerPath -PathType Leaf) $brokerPath

$app = Get-Process -Name 'Sentinel.App' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($app) {
    try {
        $appPackage = Get-ProcessPackageFullName -ProcessId $app.Id
        Add-Result 'Running Sentinel app package identity matches installed package' ($appPackage -eq $package.PackageFullName) \
            "PID=$($app.Id); ProcessPackage=$appPackage; Expected=$($package.PackageFullName)"
    }
    catch {
        Add-Result 'Running Sentinel app package identity matches installed package' $false $_.Exception.Message
    }
}
else {
    Add-Result 'Running Sentinel app package identity matches installed package' $false 'Sentinel.App is not running. Launch the installed app and rerun this validation.'
}

if ($RunElevatedProbe) {
    if (-not (Test-Path -LiteralPath $brokerPath -PathType Leaf)) {
        Add-Result 'Elevated broker package identity matches installed package' $false 'Broker executable is missing.'
        Add-Result 'Unrelated same-user pipe caller rejected' $false 'Broker executable is missing.'
    }
    else {
        $token = [Guid]::NewGuid().ToString('N')
        $pipeName = "SentinelAI.Broker.$token"
        $broker = $null
        try {
            try {
                $broker = Start-Process -FilePath $brokerPath -ArgumentList @('--pipe', $token) -Verb RunAs -PassThru -WindowStyle Hidden
            }
            catch {
                Add-Result 'Elevated broker launch/UAC' $false "UAC launch failed or was canceled: $($_.Exception.Message)"
                throw
            }

            Add-Result 'Elevated broker launch/UAC' $true "Broker PID=$($broker.Id)"

            $identityDeadline = [DateTime]::UtcNow.AddSeconds(10)
            $brokerPackage = $null
            $identityError = $null
            while ([DateTime]::UtcNow -lt $identityDeadline -and -not $brokerPackage) {
                try {
                    if ($broker.HasExited) { break }
                    $brokerPackage = Get-ProcessPackageFullName -ProcessId $broker.Id
                }
                catch {
                    $identityError = $_.Exception.Message
                    Start-Sleep -Milliseconds 200
                }
            }

            Add-Result 'Elevated broker package identity matches installed package' ($brokerPackage -eq $package.PackageFullName) $(
                if ($brokerPackage) { "ProcessPackage=$brokerPackage; Expected=$($package.PackageFullName)" }
                elseif ($identityError) { $identityError }
                else { 'Broker exited before package identity could be read.' }
            )

            $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
            try {
                $pipe.Connect(10000)
                $reader = [IO.StreamReader]::new($pipe, [Text.UTF8Encoding]::new($false), $false, 4096, $true)
                try {
                    $line = $reader.ReadLine()
                    if ([string]::IsNullOrWhiteSpace($line)) {
                        Add-Result 'Unrelated same-user pipe caller rejected' $false 'Broker returned no rejection result.'
                    }
                    else {
                        $response = $line | ConvertFrom-Json
                        $rejected = ($response.Succeeded -eq $false) -and ($response.Code -eq 'UnauthorizedCaller')
                        Add-Result 'Unrelated same-user pipe caller rejected' $rejected "Broker response: $line"
                    }
                }
                finally {
                    $reader.Dispose()
                }
            }
            finally {
                $pipe.Dispose()
            }

            try { $broker.WaitForExit(10000) | Out-Null } catch { }
            Add-Result 'Broker exits after rejecting unrelated caller' ($broker.HasExited) $(
                if ($broker.HasExited) { "ExitCode=$($broker.ExitCode)" }
                else { 'Broker remained running after the unauthorized caller was rejected.' }
            )
        }
        catch {
            if (-not ($results | Where-Object Check -eq 'Unrelated same-user pipe caller rejected')) {
                Add-Result 'Unrelated same-user pipe caller rejected' $false $_.Exception.Message
            }
        }
        finally {
            if ($broker -and -not $broker.HasExited) {
                Write-Warning "Elevated broker PID $($broker.Id) is still running and will self-timeout. This non-elevated validation script will not claim it was terminated."
            }
        }
    }
}
else {
    Add-Result 'Elevated broker package identity matches installed package' $false 'Not executed. Rerun with -RunElevatedProbe and accept the UAC prompt on the dedicated validation machine.'
    Add-Result 'Unrelated same-user pipe caller rejected' $false 'Not executed. Rerun with -RunElevatedProbe.'
}

''
'=== PRIVILEGED BROKER RUNTIME VALIDATION SUMMARY ==='
$results | Format-Table -AutoSize -Wrap
''
$failed = @($results | Where-Object Result -eq 'FAIL')
"PASS: $(@($results | Where-Object Result -eq 'PASS').Count)"
"FAIL: $($failed.Count)"

if ($failed.Count -gt 0) {
    Write-Host 'RESULT: FAIL / INCOMPLETE' -ForegroundColor Red
    exit 1
}

Write-Host 'RESULT: PASS' -ForegroundColor Green
exit 0
