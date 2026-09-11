using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Prepares and executes user-approved driver repairs. Automatic Windows Update
    /// installation is offered only when Sentinel can bind one exact PnP device and
    /// one of its hardware IDs to one exact Windows Update identity. Name-only or
    /// ambiguous matches fail closed.
    /// </summary>
    public sealed class DriverAutomaticRepairCoordinator
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);
        private readonly AuthoritativeDriverResearchService _researchService = new();
        private readonly DriverPersistentInvestigationCoordinator _persistentInvestigationCoordinator = new();
        private readonly DriverDiagnosticEvidenceCollector _evidenceCollector = new();
        private readonly StoreSubscriptionService _subscriptionService = new();

        public async Task<DriverRepairPlan> PrepareAsync(string deviceName)
        {
            DriverDiagnosticEvidence evidence = await _evidenceCollector.CollectAsync(deviceName);

            DriverRepairPlan windowsUpdatePlan = await Task.Run(() => PrepareWindowsUpdate(deviceName));
            if (windowsUpdatePlan.Available)
            {
                windowsUpdatePlan = windowsUpdatePlan with { DiagnosticEvidence = evidence.ToInvestigationSummary() };
                await PersistOutcomeSafelyAsync(deviceName, windowsUpdatePlan);
                return windowsUpdatePlan;
            }

            AuthoritativeDriverResearchService.DriverResearchResult research = await _researchService.ResearchAsync(deviceName);
            string evidenceSummary = evidence.HasMachineSpecificEvidence
                ? " Sentinel automatically collected the device identity, hardware IDs, installed driver, computer/BIOS identity, and recent relevant system events available on this computer."
                : " Sentinel attempted to collect additional machine-specific driver evidence automatically; some details were not available through the current Windows interfaces.";

            DriverRepairPlan finalPlan = !research.Completed
                ? DriverRepairPlan.Unavailable(deviceName, research.Summary + evidenceSummary, researchPerformed: true) with { DiagnosticEvidence = evidence.ToInvestigationSummary() }
                : DriverRepairPlan.Researched(deviceName, research.SourceName, research.SourceUri, research.ConfidencePercent,
                    research.Summary + evidenceSummary, research.UserActionRequired) with { DiagnosticEvidence = evidence.ToInvestigationSummary() };

            await PersistOutcomeSafelyAsync(deviceName, finalPlan);
            return finalPlan;
        }

        public async Task<DriverRepairResult> ExecuteAsync(DriverRepairPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (!plan.Available || !plan.AutomaticInstallationVerified ||
                string.IsNullOrWhiteSpace(plan.TargetDeviceInstanceId) ||
                string.IsNullOrWhiteSpace(plan.TargetHardwareId) ||
                string.IsNullOrWhiteSpace(plan.UpdateId))
            {
                return DriverRepairResult.Failed(
                    "Automatic installation is not verified",
                    "Sentinel did not install anything because the repair is not bound to one verified device, hardware ID, and Windows Update identity.");
            }

            SubscriptionState subscription = await _subscriptionService.GetStateAsync().ConfigureAwait(false);
            if (!subscription.IsActive)
                return DriverRepairResult.Failed("Subscription required", "Sentinel can monitor and investigate the device locally, but an active subscription is required before it installs a driver repair.");

            return await Task.Run(() => Execute(plan)).ConfigureAwait(false);
        }

        private async Task PersistOutcomeSafelyAsync(string deviceName, DriverRepairPlan plan)
        {
            try { await _persistentInvestigationCoordinator.RecordResearchOutcomeAsync(deviceName, ExtractErrorCode(deviceName), plan).ConfigureAwait(false); }
            catch { }
        }

        private static string ExtractErrorCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            int codeIndex = value.LastIndexOf("(Code ", StringComparison.OrdinalIgnoreCase);
            if (codeIndex < 0) return string.Empty;
            int endIndex = value.IndexOf(')', codeIndex);
            return endIndex > codeIndex ? value[(codeIndex + 1)..endIndex].Trim() : value[(codeIndex + 1)..].Trim();
        }

        private static DriverRepairPlan PrepareWindowsUpdate(string deviceName)
        {
            string safeDeviceName = EscapePowerShellLiteral(deviceName);
            string command =
                "$deviceName='" + safeDeviceName + "'; " +
                "$devices=@(Get-CimInstance Win32_PnPEntity -ErrorAction Stop | Where-Object {$_.Name -eq $deviceName}); " +
                "if ($devices.Count -ne 1) { \"AVAILABLE=False`nREASON=DeviceIdentityAmbiguous\"; exit 0 }; " +
                "$device=$devices[0]; $instance=[string]$device.PNPDeviceID; " +
                "$hp=Get-PnpDeviceProperty -InstanceId $instance -KeyName 'DEVPKEY_Device_HardwareIds' -ErrorAction SilentlyContinue; " +
                "$hardwareIds=@(); if($hp -and $hp.Data){$hardwareIds=@($hp.Data | ForEach-Object {[string]$_} | Where-Object {-not [string]::IsNullOrWhiteSpace($_)})}; " +
                "if($hardwareIds.Count -eq 0){\"AVAILABLE=False`nREASON=HardwareIdentityUnavailable`nDEVICEID=$instance\"; exit 0}; " +
                "$driver=@(Get-CimInstance Win32_PnPSignedDriver -ErrorAction SilentlyContinue | Where-Object {$_.DeviceID -eq $instance} | Select-Object -First 1); " +
                "$version=if($driver.Count -gt 0){[string]$driver[0].DriverVersion}else{''}; " +
                "$session=New-Object -ComObject Microsoft.Update.Session; $searcher=$session.CreateUpdateSearcher(); " +
                "$result=$searcher.Search(\"IsInstalled=0 and IsHidden=0 and Type='Driver'\"); " +
                "$matches=@($result.Updates | Where-Object {$u=[string]$_.DriverHardwareID; -not [string]::IsNullOrWhiteSpace($u) -and ($hardwareIds -icontains $u)}); " +
                "if ($matches.Count -ne 1) { \"AVAILABLE=False`nREASON=HardwareBoundUpdateAmbiguousOrMissing`nDEVICEID=$instance\"; exit 0 }; " +
                "$u=$matches[0]; $matchedHardware=[string]$u.DriverHardwareID; " +
                "\"AVAILABLE=True`nTITLE=$($u.Title)`nMODEL=$($u.DriverModel)`nHARDWAREID=$matchedHardware`nUPDATEID=$($u.Identity.UpdateID)`nREVISION=$($u.Identity.RevisionNumber)`nDEVICEID=$instance`nVERSION=$version`nREBOOT=$($u.RebootRequired)\"";

            ProcessResult result = RunPowerShell(command, TimeSpan.FromSeconds(45));
            if (!result.Success)
                return DriverRepairPlan.Unavailable(deviceName, "Sentinel could not complete the Windows Update driver search. It will continue with authoritative Microsoft and manufacturer research.");

            if (!GetValue(result.Output, "AVAILABLE").Equals("True", StringComparison.OrdinalIgnoreCase))
                return DriverRepairPlan.Unavailable(deviceName, "Windows Update did not expose exactly one driver package bound to a hardware ID of the diagnosed device. Sentinel will not guess; it will continue with authoritative Microsoft and manufacturer research.");

            string deviceId = GetValue(result.Output, "DEVICEID");
            string hardwareId = GetValue(result.Output, "HARDWAREID");
            string updateId = GetValue(result.Output, "UPDATEID");
            string revisionText = GetValue(result.Output, "REVISION");
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(hardwareId) || string.IsNullOrWhiteSpace(updateId) ||
                !int.TryParse(revisionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int revision))
            {
                return DriverRepairPlan.Unavailable(deviceName, "Windows Update returned incomplete device/package identity evidence. Sentinel will not install an ambiguous driver.");
            }

            string title = GetValue(result.Output, "TITLE");
            string version = GetValue(result.Output, "VERSION");
            return new DriverRepairPlan(
                true, true, false, false, deviceName, title, "Windows Update", string.Empty, 100,
                "Windows Update package bound to the diagnosed device hardware identity",
                "Sentinel found exactly one Windows Update driver whose DriverHardwareID matches a hardware ID reported by the diagnosed device. Approval is bound to the device instance, hardware ID, and Windows Update ID/revision and will be revalidated immediately before installation.")
            {
                TargetDeviceInstanceId = deviceId,
                TargetHardwareId = hardwareId,
                UpdateId = updateId,
                UpdateRevision = revision,
                PreInstallDriverVersion = version
            };
        }

        private static DriverRepairResult Execute(DriverRepairPlan plan)
        {
            string safeDeviceId = EscapePowerShellLiteral(plan.TargetDeviceInstanceId);
            string safeHardwareId = EscapePowerShellLiteral(plan.TargetHardwareId);
            string safeUpdateId = EscapePowerShellLiteral(plan.UpdateId);
            int revision = plan.UpdateRevision;

            string command =
                "$deviceId='" + safeDeviceId + "'; $hardwareId='" + safeHardwareId + "'; $updateId='" + safeUpdateId + "'; $revision=" + revision.ToString(CultureInfo.InvariantCulture) + "; " +
                "$devices=@(Get-CimInstance Win32_PnPEntity -ErrorAction Stop | Where-Object {$_.PNPDeviceID -eq $deviceId}); " +
                "if($devices.Count -ne 1){'RESULT=DeviceChanged'; exit 5}; " +
                "$hp=Get-PnpDeviceProperty -InstanceId $deviceId -KeyName 'DEVPKEY_Device_HardwareIds' -ErrorAction SilentlyContinue; " +
                "$hardwareIds=@(); if($hp -and $hp.Data){$hardwareIds=@($hp.Data | ForEach-Object {[string]$_})}; " +
                "if(-not ($hardwareIds -icontains $hardwareId)){'RESULT=HardwareIdentityChanged'; exit 7}; " +
                "$session=New-Object -ComObject Microsoft.Update.Session; $searcher=$session.CreateUpdateSearcher(); " +
                "$result=$searcher.Search(\"IsInstalled=0 and IsHidden=0 and Type='Driver'\"); " +
                "$updates=@($result.Updates | Where-Object {$_.Identity.UpdateID -eq $updateId -and $_.Identity.RevisionNumber -eq $revision -and [string]$_.DriverHardwareID -ieq $hardwareId}); " +
                "if($updates.Count -ne 1){'RESULT=UpdateChanged'; exit 6}; $u=$updates[0]; " +
                "if(-not $u.EulaAccepted){$u.AcceptEula()}; $collection=New-Object -ComObject Microsoft.Update.UpdateColl; [void]$collection.Add($u); " +
                "$downloader=$session.CreateUpdateDownloader(); $downloader.Updates=$collection; $download=$downloader.Download(); " +
                "if($download.ResultCode -ne 2){\"RESULT=DownloadFailed`nDOWNLOADCODE=$($download.ResultCode)\"; exit 4}; " +
                "$installer=$session.CreateUpdateInstaller(); $installer.Updates=$collection; $install=$installer.Install(); $per=$install.GetUpdateResult(0); " +
                "\"RESULT=InstallReturned`nCODE=$($install.ResultCode)`nHRESULT=$($per.HResult)`nPERCODE=$($per.ResultCode)`nREBOOT=$($install.RebootRequired)\"";

            ProcessResult result = RunPowerShell(command, CommandTimeout);
            if (!result.Success || !GetValue(result.Output, "RESULT").Equals("InstallReturned", StringComparison.OrdinalIgnoreCase))
                return DriverRepairResult.Failed("Driver repair was not completed", "Sentinel did not verify a completed driver installation for the approved hardware identity. No success claim was recorded.");

            string overallCode = GetValue(result.Output, "CODE");
            string perCode = GetValue(result.Output, "PERCODE");
            string hresult = GetValue(result.Output, "HRESULT");
            bool restartRequired = GetValue(result.Output, "REBOOT").Equals("True", StringComparison.OrdinalIgnoreCase);

            if (overallCode == "3" || perCode == "3")
                return new DriverRepairResult(false, restartRequired, "Driver installation partially completed", $"Windows Update reported success with errors (HRESULT {hresult}). Sentinel will not mark the device repaired until post-restart/post-install verification succeeds.", DriverRepairOutcome.Partial);
            if (overallCode != "2" || perCode != "2" || (!string.IsNullOrWhiteSpace(hresult) && !hresult.Equals("0", StringComparison.OrdinalIgnoreCase)))
                return DriverRepairResult.Failed("Driver installation failed", $"Windows Update did not report clean success (overall {overallCode}, package {perCode}, HRESULT {hresult}). Sentinel did not mark the repair complete.");

            if (restartRequired)
                return new DriverRepairResult(true, true, "Driver installed — restart required", "Windows Update reported a successful installation, but the repair is not considered fully verified until the computer restarts and Sentinel rechecks the exact device.", DriverRepairOutcome.RebootRequired);

            VerificationResult verification = VerifyPostInstall(plan);
            if (!verification.Verified)
                return new DriverRepairResult(false, false, "Driver installed but verification is incomplete", verification.Summary, DriverRepairOutcome.Unverified);
            return new DriverRepairResult(true, false, "Driver installed and verified", verification.Summary, DriverRepairOutcome.Verified);
        }

        private static VerificationResult VerifyPostInstall(DriverRepairPlan plan)
        {
            string safeDeviceId = EscapePowerShellLiteral(plan.TargetDeviceInstanceId);
            string safeUpdateId = EscapePowerShellLiteral(plan.UpdateId);
            string safeHardwareId = EscapePowerShellLiteral(plan.TargetHardwareId);
            string command =
                "$deviceId='" + safeDeviceId + "'; $updateId='" + safeUpdateId + "'; $hardwareId='" + safeHardwareId + "'; " +
                "$device=@(Get-CimInstance Win32_PnPEntity -ErrorAction Stop | Where-Object {$_.PNPDeviceID -eq $deviceId}); " +
                "if($device.Count -ne 1){'VERIFIED=False'; exit 0}; " +
                "$hp=Get-PnpDeviceProperty -InstanceId $deviceId -KeyName 'DEVPKEY_Device_HardwareIds' -ErrorAction SilentlyContinue; $ids=@(); if($hp -and $hp.Data){$ids=@($hp.Data | ForEach-Object {[string]$_})}; " +
                "if(-not ($ids -icontains $hardwareId)){'VERIFIED=False'; exit 0}; " +
                "$driver=@(Get-CimInstance Win32_PnPSignedDriver -ErrorAction SilentlyContinue | Where-Object {$_.DeviceID -eq $deviceId} | Select-Object -First 1); " +
                "$session=New-Object -ComObject Microsoft.Update.Session; $searcher=$session.CreateUpdateSearcher(); $r=$searcher.Search(\"IsInstalled=0 and IsHidden=0 and Type='Driver'\"); " +
                "$stillOffered=@($r.Updates | Where-Object {$_.Identity.UpdateID -eq $updateId -and [string]$_.DriverHardwareID -ieq $hardwareId}).Count -gt 0; " +
                "$version=if($driver.Count -gt 0){[string]$driver[0].DriverVersion}else{''}; " +
                "\"VERIFIED=$(((-not $stillOffered) -and $device[0].ConfigManagerErrorCode -eq 0))`nVERSION=$version`nCODE=$($device[0].ConfigManagerErrorCode)\"";

            ProcessResult result = RunPowerShell(command, TimeSpan.FromSeconds(45));
            if (!result.Success || !GetValue(result.Output, "VERIFIED").Equals("True", StringComparison.OrdinalIgnoreCase))
                return new(false, "Windows Update returned success, but Sentinel could not verify that the exact hardware-bound device is healthy and the approved update is no longer pending.");

            string version = GetValue(result.Output, "VERSION");
            return new(true, string.IsNullOrWhiteSpace(version)
                ? "Sentinel verified that the exact hardware-bound device is healthy and the approved update is no longer pending."
                : $"Sentinel verified that the exact hardware-bound device is healthy and the approved update is no longer pending. Current driver version: {version}.");
        }

        private static ProcessResult RunPowerShell(string command, TimeSpan timeout)
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            ProcessStartInfo startInfo = new()
            {
                FileName = ResolvePowerShellPath(),
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            ProcessExecutionResult result = BoundedProcessRunner.RunAsync(startInfo, timeout).GetAwaiter().GetResult();
            return new(result.Succeeded, result.StandardOutput.Trim(), result.Outcome, result.StandardError.Trim());
        }

        private static string ResolvePowerShellPath()
        {
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return string.IsNullOrWhiteSpace(system) ? "powershell.exe" : System.IO.Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        }

        private static string GetValue(string output, string name)
        {
            foreach (string line in (output ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return line[(name.Length + 1)..].Trim();
            return string.Empty;
        }

        private static string EscapePowerShellLiteral(string value) => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

        public sealed record DriverRepairPlan(bool Available, bool AutomaticInstallationVerified, bool ResearchPerformed, bool UserActionRequired, string DeviceName, string PackageTitle, string Source, string SourceUri, int ConfidencePercent, string TrustStatement, string Summary)
        {
            public string DiagnosticEvidence { get; init; } = string.Empty;
            public string TargetDeviceInstanceId { get; init; } = string.Empty;
            public string TargetHardwareId { get; init; } = string.Empty;
            public string UpdateId { get; init; } = string.Empty;
            public int UpdateRevision { get; init; }
            public string PreInstallDriverVersion { get; init; } = string.Empty;
            public static DriverRepairPlan Unavailable(string deviceName, string summary, bool researchPerformed = false) => new(false, false, researchPerformed, false, deviceName, string.Empty, string.Empty, string.Empty, 0, string.Empty, summary);
            public static DriverRepairPlan Researched(string deviceName, string source, string sourceUri, int confidencePercent, string summary, bool userActionRequired) => new(false, false, true, userActionRequired, deviceName, string.Empty, source, sourceUri, confidencePercent, "Authoritative Microsoft or computer-manufacturer source", summary);
        }

        public enum DriverRepairOutcome { Failed, Partial, RebootRequired, Unverified, Verified }

        public sealed record DriverRepairResult(bool Success, bool RestartRequired, string Title, string Summary, DriverRepairOutcome Outcome = DriverRepairOutcome.Failed)
        {
            public static DriverRepairResult Failed(string title, string summary) => new(false, false, title, summary, DriverRepairOutcome.Failed);
        }

        private sealed record ProcessResult(bool Success, string Output, ProcessExecutionOutcome Outcome, string Error);
        private sealed record VerificationResult(bool Verified, string Summary);
    }
}
