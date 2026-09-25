/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Read-only Microsoft Defender evidence provider. The provider never invokes
    /// Defender remediation. It returns active threat/detection state for Sentinel's
    /// separate investigation and approval policy.
    /// </summary>
    public sealed class DefenderThreatEvidenceProvider
    {
        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);

        public DefenderThreatEvidenceParser.DefenderThreatEvidenceSnapshot GetSnapshot()
        {
            if (!OperatingSystem.IsWindows())
                return DefenderThreatEvidenceParser.DefenderThreatEvidenceSnapshot.Unavailable(
                    "Microsoft Defender threat evidence is only available on Windows.");

            const string command = @"
$ErrorActionPreference = 'Stop'
$active = @(Get-MpThreat -ErrorAction Stop | Where-Object { $_.IsActive -eq $true })
$items = @(
    foreach ($t in $active) {
        $matching = @(
            Get-MpThreatDetection -ThreatID ([Int64]$t.ThreatID) -ErrorAction Stop |
                ForEach-Object {
                    [pscustomobject]@{
                        ThreatStatusID = [int]$_.ThreatStatusID
                        CurrentThreatExecutionStatusID = [int]$_.CurrentThreatExecutionStatusID
                        ActionSuccess = [bool]$_.ActionSuccess
                        InitialDetectionTime = if ($null -ne $_.InitialDetectionTime) { $_.InitialDetectionTime.ToUniversalTime().ToString('o') } else { $null }
                        LastThreatStatusChangeTime = if ($null -ne $_.LastThreatStatusChangeTime) { $_.LastThreatStatusChangeTime.ToUniversalTime().ToString('o') } else { $null }
                        Resources = @($_.Resources | ForEach-Object { [string]$_ })
                    }
                }
        )
        [pscustomobject]@{
            ThreatID = [Int64]$t.ThreatID
            ThreatName = [string]$t.ThreatName
            SeverityID = [int]$t.SeverityID
            DidThreatExecute = [bool]$t.DidThreatExecute
            IsActive = [bool]$t.IsActive
            Resources = @($t.Resources | ForEach-Object { [string]$_ })
            Detections = $matching
        }
    }
)
[pscustomobject]@{
    Available = $true
    Threats = $items
} | ConvertTo-Json -Depth 6 -Compress
";

            try
            {
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                ProcessStartInfo startInfo = new()
                {
                    FileName = ResolvePowerShellPath(),
                    Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                ProcessExecutionResult result = BoundedProcessRunner.RunAsync(
                    startInfo,
                    QueryTimeout,
                    maxOutputChars: 256_000).GetAwaiter().GetResult();

                if (!result.Succeeded)
                {
                    return DefenderThreatEvidenceParser.DefenderThreatEvidenceSnapshot.Unavailable(
                        $"Microsoft Defender active-threat evidence could not be collected ({result.Outcome}).");
                }

                return DefenderThreatEvidenceParser.Parse(result.StandardOutput);
            }
            catch (Exception ex)
            {
                return DefenderThreatEvidenceParser.DefenderThreatEvidenceSnapshot.Unavailable(
                    $"Microsoft Defender active-threat evidence could not be collected safely ({ex.GetType().Name}).");
            }
        }

        private static string ResolvePowerShellPath()
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return string.IsNullOrWhiteSpace(system)
                ? "powershell.exe"
                : Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        }
    }
}
