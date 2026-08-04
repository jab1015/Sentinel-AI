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
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

        public string GetWindowsUpdateStatus()
        {
            string value = RunPowerShell(
                "$service=Get-Service -Name wuauserv -ErrorAction Stop; " +
                "$latest=Get-HotFix -ErrorAction SilentlyContinue | Sort-Object InstalledOn -Descending | Select-Object -First 1; " +
                "if ($null -eq $latest) { \"Service=$($service.Status);LatestInstalledUpdate=Unavailable\" } " +
                "else { \"Service=$($service.Status);LatestInstalledUpdate=$($latest.HotFixID);InstalledOn=$($latest.InstalledOn.ToString('yyyy-MM-dd'))\" }");

            bool? restart = TryGetRestartPending();
            string restartText = restart switch
            {
                true => "A Windows restart is pending.",
                false => "No verified local restart indicator is currently present.",
                null => "Sentinel could not verify whether Windows requires a restart."
            };

            return string.IsNullOrWhiteSpace(value)
                ? $"Sentinel could not verify Windows Update status. {restartText}"
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

            return string.IsNullOrWhiteSpace(value)
                ? "Sentinel could not verify TPM status on this computer."
                : $"Verified TPM status: {value.Replace(';', ',')}.";
        }

        public string GetSecureBootStatus()
        {
            string value = RunPowerShell(
                "if (Confirm-SecureBootUEFI -ErrorAction Stop) { 'Enabled' } else { 'Disabled' }");

            return string.IsNullOrWhiteSpace(value)
                ? "Sentinel could not verify Secure Boot status. This can occur when Windows is not running in UEFI mode or access is unavailable."
                : $"Verified Secure Boot status: {value}.";
        }

        public string GetBitLockerStatus()
        {
            string value = RunPowerShell(
                "$v=Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop; " +
                "\"Protection=$($v.ProtectionStatus);Volume=$($v.VolumeStatus);Encryption=$($v.EncryptionPercentage)%\"");

            return string.IsNullOrWhiteSpace(value)
                ? "Sentinel could not verify BitLocker or device-encryption status for the Windows drive."
                : $"Verified Windows drive encryption status: {value.Replace(';', ',')}.";
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
            try
            {
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                using Process process = new();
                var output = new StringBuilder();
                var error = new StringBuilder();

                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
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

                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        error.AppendLine(e.Data);
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
                return process.ExitCode == 0 && error.Length == 0
                    ? output.ToString().Trim()
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
