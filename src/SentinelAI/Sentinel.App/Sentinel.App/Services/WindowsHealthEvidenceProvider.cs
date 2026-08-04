/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Collects narrowly scoped Windows health evidence from local Windows interfaces.
    /// Every result is fail-closed when Windows does not expose a verifiable value.
    /// </summary>
    public sealed class WindowsHealthEvidenceProvider
    {
        public string GetWindowsUpdateStatus()
        {
            string service = RunPowerShell("(Get-Service -Name wuauserv -ErrorAction Stop).Status.ToString()");
            bool restart = IsRestartPending();
            return string.IsNullOrWhiteSpace(service)
                ? "Sentinel could not verify Windows Update service status."
                : $"Windows Update service is {service}. A Windows restart is {(restart ? "pending" : "not currently pending")} based on verified local restart indicators.";
        }

        public string GetPendingRestartStatus() =>
            IsRestartPending()
                ? "Windows has verified local indicators showing that a restart is pending."
                : "Sentinel found no verified local Windows indicators requiring a restart.";

        public string GetTpmStatus()
        {
            string value = RunPowerShell("$t=Get-Tpm -ErrorAction Stop; \"Present=$($t.TpmPresent);Ready=$($t.TpmReady);Enabled=$($t.TpmEnabled);Activated=$($t.TpmActivated)\"");
            return string.IsNullOrWhiteSpace(value)
                ? "Sentinel could not verify TPM status on this computer."
                : $"Verified TPM status: {value.Replace(';', ',')}.";
        }

        public string GetSecureBootStatus()
        {
            string value = RunPowerShell("try { if (Confirm-SecureBootUEFI -ErrorAction Stop) { 'Enabled' } else { 'Disabled' } } catch { 'Unavailable: ' + $_.Exception.Message }");
            return string.IsNullOrWhiteSpace(value)
                ? "Sentinel could not verify Secure Boot status."
                : $"Secure Boot is {value}.";
        }

        public string GetBitLockerStatus()
        {
            string value = RunPowerShell("$v=Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop; \"Protection=$($v.ProtectionStatus);Volume=$($v.VolumeStatus);Encryption=$($v.EncryptionPercentage)%\"");
            return string.IsNullOrWhiteSpace(value)
                ? "Sentinel could not verify BitLocker or device-encryption status for the Windows drive."
                : $"Verified Windows drive encryption status: {value.Replace(';', ',')}.";
        }

        private static bool IsRestartPending()
        {
            return Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is not null
                || Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") is not null
                || Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager")?.GetValue("PendingFileRenameOperations") is not null;
        }

        private static string RunPowerShell(string command)
        {
            try
            {
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                process.Start();
                if (!process.WaitForExit(5000))
                {
                    process.Kill(true);
                    return string.Empty;
                }

                return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
