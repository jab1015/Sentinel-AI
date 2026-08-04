/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Text;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Collects local Windows driver and Plug-and-Play evidence without guessing.
    /// </summary>
    public sealed class DriverHealthEvidenceProvider
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

        public string GetDriverHealthStatus()
        {
            string value = RunPowerShell(
                "$devices=@(Get-CimInstance Win32_PnPEntity -ErrorAction Stop); " +
                "$problems=@($devices | Where-Object {$_.ConfigManagerErrorCode -ne 0}); " +
                "$signed=@(Get-CimInstance Win32_PnPSignedDriver -ErrorAction SilentlyContinue); " +
                "$unsigned=@($signed | Where-Object {$_.IsSigned -eq $false}); " +
                "$recent=@(Get-WinEvent -FilterHashtable @{LogName='System';StartTime=(Get-Date).AddDays(-7)} -ErrorAction SilentlyContinue | " +
                "Where-Object {$_.ProviderName -match 'Kernel-PnP|DriverFrameworks|UserPnp'} | Select-Object -First 5); " +
                "$problemText=@($problems | Select-Object -First 5 | ForEach-Object {\"$($_.Name) (Code $($_.ConfigManagerErrorCode))\"}) -join ' | '; " +
                "$eventText=@($recent | Select-Object -First 3 | ForEach-Object {\"$($_.ProviderName) Event $($_.Id)\"}) -join ' | '; " +
                "\"Devices=$($devices.Count);ProblemDevices=$($problems.Count);UnsignedDrivers=$($unsigned.Count);RecentDriverEvents=$($recent.Count);Problems=$problemText;Events=$eventText\"");

            if (string.IsNullOrWhiteSpace(value))
            {
                return "Sentinel could not verify driver health because Windows did not expose Plug-and-Play driver evidence to this process.";
            }

            return $"Verified driver health evidence: {Normalize(value)}.";
        }

        private static string RunPowerShell(string command)
        {
            try
            {
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                if (!process.Start()) return string.Empty;
                string output = process.StandardOutput.ReadToEnd();
                _ = process.StandardError.ReadToEnd();

                if (!process.WaitForExit((int)CommandTimeout.TotalMilliseconds))
                {
                    process.Kill(true);
                    return string.Empty;
                }

                return process.ExitCode == 0 ? output.Trim() : string.Empty;
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

            return normalized.Length <= 700 ? normalized : normalized[..697] + "...";
        }
    }
}
