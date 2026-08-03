/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Inspects the Windows system drive before Sentinel considers any drive
    /// optimization. This service is observational only and never runs defrag,
    /// retrim, format, repair, or other storage-changing commands.
    /// </summary>
    public sealed class StorageOptimizationAssessmentService
    {
        public async Task<StorageOptimizationAssessment> AssessSystemDriveAsync(
            CancellationToken cancellationToken = default)
        {
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            string driveLetter = systemRoot.TrimEnd('\\').TrimEnd(':');

            string mediaType = "Unknown";
            string busType = "Unknown";
            string healthStatus = "Unknown";
            bool trimEnabled = false;
            bool trimKnown = false;

            try
            {
                string ps =
                    "$p=Get-Partition -DriveLetter '" + EscapePowerShell(driveLetter) + "' -ErrorAction Stop;" +
                    "$d=$p|Get-Disk -ErrorAction Stop;" +
                    "$pd=Get-PhysicalDisk -ErrorAction SilentlyContinue | Where-Object { $_.DeviceId -eq [string]$d.Number } | Select-Object -First 1;" +
                    "if($pd){$pd.MediaType.ToString()+'|'+$pd.BusType.ToString()+'|'+$pd.HealthStatus.ToString()}else{'Unknown|'+$d.BusType.ToString()+'|Unknown'}";

                CommandResult diskResult = await RunAsync(
                    "powershell.exe",
                    $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{ps}\"",
                    cancellationToken).ConfigureAwait(false);

                if (diskResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(diskResult.Output))
                {
                    string[] parts = diskResult.Output.Trim().Split('|', StringSplitOptions.TrimEntries);
                    if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0])) mediaType = parts[0];
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])) busType = parts[1];
                    if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2])) healthStatus = parts[2];
                }
            }
            catch
            {
                // Unknown media type is safer than guessing SSD/HDD behavior.
            }

            try
            {
                CommandResult trimResult = await RunAsync(
                    "fsutil.exe",
                    "behavior query DisableDeleteNotify",
                    cancellationToken).ConfigureAwait(false);

                if (trimResult.ExitCode == 0)
                {
                    string output = trimResult.Output;
                    bool ntfsKnown = output.Contains("NTFS DisableDeleteNotify", StringComparison.OrdinalIgnoreCase);
                    bool refsKnown = output.Contains("ReFS DisableDeleteNotify", StringComparison.OrdinalIgnoreCase);
                    trimKnown = ntfsKnown || refsKnown;

                    // Windows reports 0 when delete notifications (TRIM/unmap) are enabled.
                    trimEnabled = output.Contains("DisableDeleteNotify = 0", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Leave trim state unknown rather than infer from hardware type.
            }

            StorageMediaKind kind = Classify(mediaType, busType);
            string recommendation = kind switch
            {
                StorageMediaKind.SolidState when trimKnown && trimEnabled =>
                    "Use Windows retrim/Optimize Drives only when Windows reports it is due. Do not schedule routine traditional defragmentation.",
                StorageMediaKind.SolidState when trimKnown && !trimEnabled =>
                    "TRIM is not currently verified as enabled. Sentinel should investigate this before attempting SSD optimization.",
                StorageMediaKind.HardDisk =>
                    "Traditional defragmentation may be appropriate, but Sentinel should first analyze fragmentation and run it only when Windows reports a meaningful benefit.",
                _ =>
                    "Drive type could not be verified. Sentinel will not choose defrag or retrim automatically until the media type is known."
            };

            bool safeToConsiderNativeOptimization =
                !healthStatus.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase) &&
                !healthStatus.Equals("Warning", StringComparison.OrdinalIgnoreCase) &&
                kind != StorageMediaKind.Unknown;

            return new StorageOptimizationAssessment(
                DriveLetter: driveLetter,
                MediaKind: kind,
                ReportedMediaType: mediaType,
                BusType: busType,
                HealthStatus: healthStatus,
                TrimStateKnown: trimKnown,
                TrimEnabled: trimEnabled,
                SafeToConsiderNativeOptimization: safeToConsiderNativeOptimization,
                Recommendation: recommendation);
        }

        private static StorageMediaKind Classify(string mediaType, string busType)
        {
            if (mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase))
                return StorageMediaKind.SolidState;

            if (mediaType.Contains("HDD", StringComparison.OrdinalIgnoreCase))
                return StorageMediaKind.HardDisk;

            if (busType.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                return StorageMediaKind.SolidState;

            return StorageMediaKind.Unknown;
        }

        private static async Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            CancellationToken cancellationToken)
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new CommandResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }

        private static string EscapePowerShell(string value) => value.Replace("'", "''");

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record StorageOptimizationAssessment(
        string DriveLetter,
        StorageMediaKind MediaKind,
        string ReportedMediaType,
        string BusType,
        string HealthStatus,
        bool TrimStateKnown,
        bool TrimEnabled,
        bool SafeToConsiderNativeOptimization,
        string Recommendation);

    public enum StorageMediaKind
    {
        Unknown,
        SolidState,
        HardDisk
    }
}
