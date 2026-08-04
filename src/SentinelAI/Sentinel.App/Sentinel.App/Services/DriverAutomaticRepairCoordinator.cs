using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Prepares and executes user-approved driver repairs. Windows Update is
    /// preferred; authoritative Microsoft/OEM research is used when it cannot
    /// provide a compatible package. Sentinel never restarts Windows without
    /// separate user approval.
    /// </summary>
    public sealed class DriverAutomaticRepairCoordinator
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);
        private readonly AuthoritativeDriverResearchService _researchService = new();

        public async Task<DriverRepairPlan> PrepareAsync(string deviceName)
        {
            DriverRepairPlan windowsUpdatePlan = await Task.Run(() => PrepareWindowsUpdate(deviceName));
            if (windowsUpdatePlan.Available)
            {
                return windowsUpdatePlan;
            }

            AuthoritativeDriverResearchService.DriverResearchResult research =
                await _researchService.ResearchAsync(deviceName);

            if (!research.Completed)
            {
                return DriverRepairPlan.Unavailable(
                    deviceName,
                    research.Summary,
                    researchPerformed: true);
            }

            return DriverRepairPlan.Researched(
                deviceName,
                research.SourceName,
                research.SourceUri,
                research.ConfidencePercent,
                research.Summary,
                research.UserActionRequired);
        }

        public Task<DriverRepairResult> ExecuteAsync(DriverRepairPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (!plan.Available || !plan.AutomaticInstallationVerified)
            {
                return Task.FromResult(new DriverRepairResult(
                    false,
                    false,
                    "Automatic installation is not verified",
                    "Sentinel did not install anything because this repair plan is research guidance, not a verified automatic installation package."));
            }

            return Task.Run(() => Execute(plan));
        }

        private static DriverRepairPlan PrepareWindowsUpdate(string deviceName)
        {
            string command =
                "$session=New-Object -ComObject Microsoft.Update.Session; " +
                "$searcher=$session.CreateUpdateSearcher(); " +
                "$result=$searcher.Search(\"IsInstalled=0 and IsHidden=0 and Type='Driver'\"); " +
                "$match=@($result.Updates | Where-Object {$_.Title -match 'Intel|Management Engine|Chipset'} | Select-Object -First 1); " +
                "if ($match.Count -eq 0) { 'AVAILABLE=False' } else { " +
                "$u=$match[0]; " +
                "\"AVAILABLE=True`nTITLE=$($u.Title)`nREBOOT=$($u.RebootRequired)`nEULA=$($u.EulaAccepted)\" }";

            ProcessResult result = RunPowerShell(command, TimeSpan.FromSeconds(45));
            if (!result.Success)
            {
                return DriverRepairPlan.Unavailable(
                    deviceName,
                    "Sentinel could not complete the Windows Update driver search. It will continue with authoritative Microsoft and manufacturer research.");
            }

            bool available = GetValue(result.Output, "AVAILABLE").Equals("True", StringComparison.OrdinalIgnoreCase);
            if (!available)
            {
                return DriverRepairPlan.Unavailable(
                    deviceName,
                    "Windows Update did not offer a compatible driver repair. Sentinel will continue with authoritative Microsoft and manufacturer research.");
            }

            string title = GetValue(result.Output, "TITLE");
            return new DriverRepairPlan(
                true,
                true,
                false,
                false,
                deviceName,
                title,
                "Windows Update",
                string.Empty,
                100,
                "Microsoft-signed Windows Update driver package",
                "Sentinel found a compatible signed driver through Windows Update. It can download and install this package after your approval. It will not restart the computer without separate approval.");
        }

        private static DriverRepairResult Execute(DriverRepairPlan plan)
        {
            string safeTitle = EscapePowerShellLiteral(plan.PackageTitle);
            string command =
                "$title='" + safeTitle + "'; " +
                "$session=New-Object -ComObject Microsoft.Update.Session; " +
                "$searcher=$session.CreateUpdateSearcher(); " +
                "$result=$searcher.Search(\"IsInstalled=0 and IsHidden=0 and Type='Driver'\"); " +
                "$update=@($result.Updates | Where-Object {$_.Title -eq $title} | Select-Object -First 1); " +
                "if ($update.Count -eq 0) { 'RESULT=NotFound'; exit 3 }; " +
                "$u=$update[0]; if (-not $u.EulaAccepted) {$u.AcceptEula()}; " +
                "$collection=New-Object -ComObject Microsoft.Update.UpdateColl; [void]$collection.Add($u); " +
                "$downloader=$session.CreateUpdateDownloader(); $downloader.Updates=$collection; " +
                "$download=$downloader.Download(); if ($download.ResultCode -notin 2,3) { \"RESULT=DownloadFailed`nCODE=$($download.ResultCode)\"; exit 4 }; " +
                "$installer=$session.CreateUpdateInstaller(); $installer.Updates=$collection; " +
                "$install=$installer.Install(); " +
                "\"RESULT=Installed`nCODE=$($install.ResultCode)`nREBOOT=$($install.RebootRequired)\"";

            ProcessResult result = RunPowerShell(command, CommandTimeout);
            if (!result.Success || !GetValue(result.Output, "RESULT").Equals("Installed", StringComparison.OrdinalIgnoreCase))
            {
                return new DriverRepairResult(
                    false,
                    false,
                    "Driver repair was not completed",
                    "Sentinel did not install the driver. No restart was requested. Review Windows Update and the Activity Center for details.");
            }

            bool restartRequired = GetValue(result.Output, "REBOOT").Equals("True", StringComparison.OrdinalIgnoreCase);
            return new DriverRepairResult(
                true,
                restartRequired,
                restartRequired ? "Driver installed — restart required" : "Driver installed",
                restartRequired
                    ? "Sentinel installed the signed driver. Save your work, then restart when you are ready. Sentinel will verify the device after Windows starts again."
                    : "Sentinel installed the signed driver. Sentinel will refresh local evidence and verify the device now.");
        }

        private static ProcessResult RunPowerShell(string command, TimeSpan timeout)
        {
            try
            {
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                if (!process.Start()) return new(false, string.Empty);
                string output = process.StandardOutput.ReadToEnd();
                _ = process.StandardError.ReadToEnd();
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    process.Kill(true);
                    return new(false, output);
                }
                return new(process.ExitCode == 0, output.Trim());
            }
            catch
            {
                return new(false, string.Empty);
            }
        }

        private static string GetValue(string output, string name)
        {
            foreach (string line in (output ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return line[(name.Length + 1)..].Trim();
                }
            }
            return string.Empty;
        }

        private static string EscapePowerShellLiteral(string value) =>
            (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

        public sealed record DriverRepairPlan(
            bool Available,
            bool AutomaticInstallationVerified,
            bool ResearchPerformed,
            bool UserActionRequired,
            string DeviceName,
            string PackageTitle,
            string Source,
            string SourceUri,
            int ConfidencePercent,
            string TrustStatement,
            string Summary)
        {
            public static DriverRepairPlan Unavailable(string deviceName, string summary, bool researchPerformed = false) =>
                new(false, false, researchPerformed, false, deviceName, string.Empty, string.Empty, string.Empty, 0, string.Empty, summary);

            public static DriverRepairPlan Researched(
                string deviceName,
                string source,
                string sourceUri,
                int confidencePercent,
                string summary,
                bool userActionRequired) =>
                new(false, false, true, userActionRequired, deviceName, string.Empty, source, sourceUri, confidencePercent,
                    "Authoritative Microsoft or computer-manufacturer source", summary);
        }

        public sealed record DriverRepairResult(
            bool Success,
            bool RestartRequired,
            string Title,
            string Summary);

        private sealed record ProcessResult(bool Success, string Output);
    }
}
