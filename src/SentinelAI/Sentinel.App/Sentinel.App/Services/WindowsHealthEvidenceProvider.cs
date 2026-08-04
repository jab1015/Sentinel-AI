/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Collects narrowly scoped Windows health evidence from local Windows interfaces.
    /// Every result fails closed when Windows does not expose a verifiable value.
    /// </summary>
    public sealed class WindowsHealthEvidenceProvider
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(12);

        public string GetWindowsUpdateStatus()
        {
            string value = RunPowerShell(
                "$service=Get-Service -Name wuauserv -ErrorAction Stop; " +
                "$latest=(Get-CimInstance -ClassName Win32_QuickFixEngineering -ErrorAction SilentlyContinue | " +
                "Where-Object {$_.InstalledOn} | Sort-Object {[datetime]$_.InstalledOn} -Descending | Select-Object -First 1); " +
                "if ($null -eq $latest) { \"Service=$($service.Status);LatestInstalledUpdate=Unavailable\" } " +
                "else { \"Service=$($service.Status);LatestInstalledUpdate=$($latest.HotFixID);InstalledOn=$($latest.InstalledOn)\" }");

            if (string.IsNullOrWhiteSpace(value))
            {
                value = RunPowerShell(
                    "$service=Get-Service -Name wuauserv -ErrorAction Stop; \"Service=$($service.Status)\"");
            }

            bool? restart = TryGetRestartPending();
            string restartText = restart switch
            {
                true => "A Windows restart is pending.",
                false => "No verified local restart indicator is currently present.",
                null => "Sentinel could not verify whether Windows requires a restart."
            };

            return string.IsNullOrWhiteSpace(value)
                ? $"Sentinel could not verify Windows Update service status. {restartText}"
                : $"Verified Windows Update status: {value.Replace(';', ',')}. {restartText}";
        }

        public string GetPendingRestartStatus()
        {
            bool? restart = TryGetRestartPending();
            return restart switch
            {
                true => "Windows has verified local indicators showing that a restart is pending.",
                false => "Sentinel found no verified local Windows indicators requiring a restart.",
                null => "Sentinel could not verify pending-restart status on this computer."
            };
        }

        public string GetTpmStatus()
        {
            string value = RunPowerShell(
                "$t=Get-Tpm -ErrorAction Stop; " +
                "\"Present=$($t.TpmPresent);Ready=$($t.TpmReady);Enabled=$($t.TpmEnabled);Activated=$($t.TpmActivated)\"");

            if (string.IsNullOrWhiteSpace(value))
            {
                value = RunPowerShell(
                    "$t=Get-CimInstance -Namespace root/CIMV2/Security/MicrosoftTpm -ClassName Win32_Tpm -ErrorAction Stop; " +
                    "\"Present=True;Enabled=$($t.IsEnabled_InitialValue);Activated=$($t.IsActivated_InitialValue);Owned=$($t.IsOwned_InitialValue)\"");
            }

            return string.IsNullOrWhiteSpace(value)
                ? "Sentinel could not verify TPM status because Windows did not expose TPM evidence to this process."
                : $"Verified TPM status: {value.Replace(';', ',')}.";
        }

        public string GetSecureBootStatus()
        {
            string value = RunPowerShell(
                "if (Confirm-SecureBootUEFI -ErrorAction Stop) { 'Enabled' } else { 'Disabled' }");

            if (string.IsNullOrWhiteSpace(value))
            {
                value = RunPowerShell(
                    "$v=(Get-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State' -Name UEFISecureBootEnabled -ErrorAction Stop).UEFISecureBootEnabled; " +
                    "if ($v -eq 1) {'Enabled'} elseif ($v -eq 0) {'Disabled'} else {'Unknown'}");
            }

            return string.IsNullOrWhiteSpace(value)
                ? "Sentinel could not verify Secure Boot status because Windows did not expose UEFI Secure Boot evidence to this process."
                : $"Verified Secure Boot status: {value}.";
        }

        public string GetBitLockerStatus()
        {
            string value = RunPowerShell(
                "$v=Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop; " +
                "\"Protection=$($v.ProtectionStatus);Volume=$($v.VolumeStatus);Encryption=$($v.EncryptionPercentage)%\"");

            if (string.IsNullOrWhiteSpace(value))
            {
                value = RunPowerShell(
                    "$v=Get-CimInstance -Namespace root/CIMV2/Security/MicrosoftVolumeEncryption -ClassName Win32_EncryptableVolume -Filter \"DriveLetter='$env:SystemDrive'\" -ErrorAction Stop; " +
                    "$p=$v.GetProtectionStatus().ProtectionStatus; $c=$v.GetConversionStatus(); " +
                    "\"ProtectionStatus=$p;ConversionStatus=$($c.ConversionStatus);Encryption=$($c.EncryptionPercentage)%\"");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                value = RunProcess("manage-bde.exe", "-status " + Environment.SystemDirectory[..2]);
            }

            return string.IsNullOrWhiteSpace(value)
                ? "Sentinel could not verify BitLocker or device-encryption status because Windows did not expose drive-encryption evidence to this process."
                : $"Verified Windows drive encryption status: {Normalize(value)}.";
        }

        private static bool? TryGetRestartPending()
        {
            try
            {
                using RegistryKey? updateRestart = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                using RegistryKey? servicingRestart = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                using RegistryKey? sessionManager = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager");

                return updateRestart is not null
                    || servicingRestart is not null
                    || sessionManager?.GetValue("PendingFileRenameOperations") is not null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (System.Security.SecurityException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string RunPowerShell(string command)
        {
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            return RunProcess(
                "powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}");
        }

        private static string RunProcess(string fileName, string arguments)
        {
            try
            {
                using Process process = new();
                var output = new StringBuilder();

                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        output.AppendLine(e.Data);
                    }
                };

                if (!process.Start())
                {
                    return string.Empty;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit((int)CommandTimeout.TotalMilliseconds))
                {
                    process.Kill(true);
                    return string.Empty;
                }

                process.WaitForExit();
                return process.ExitCode == 0
                    ? output.ToString().Trim()
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Normalize(string value)
        {
            string normalized = value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Replace(';', ',')
                .Trim();

            while (normalized.Contains("  ", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
            }

            return normalized.Length <= 500
                ? normalized
                : normalized[..497] + "...";
        }
    }
}
