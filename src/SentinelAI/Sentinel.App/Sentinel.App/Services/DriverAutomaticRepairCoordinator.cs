using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Prepares and executes user-approved driver repairs through the signed
    /// Windows Update channel. It never restarts Windows automatically.
    /// </summary>
    public sealed class DriverAutomaticRepairCoordinator
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

        public Task<DriverRepairPlan> PrepareAsync(string deviceName)
        {
            return Task.Run(() => Prepare(deviceName));
        }

        public Task<DriverRepairResult> ExecuteAsync(DriverRepairPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            return Task.Run(() => Execute(plan));
        }

        private static DriverRepairPlan Prepare(string deviceName)
        {
            string safeDevice = EscapePowerShellLiteral(deviceName);
            string command =
                "$device='" + safeDevice + "'; " +
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
                    "Sentinel could not complete a Windows Update driver search. No change was made.");
            }

            bool available = GetValue(result.Output, "AVAILABLE").Equals("True", StringComparison.OrdinalIgnoreCase);
            if (!available)
            {
                return DriverRepairPlan.Unavailable(
                    deviceName,
                    "Windows Update did not offer a compatible driver repair for this device. Sentinel will continue investigating other authoritative sources.");
            }

            string title = GetValue(result.Output, "TITLE");
            return new DriverRepairPlan(
                true,
                deviceName,
                title,
                "Windows Update",
                "Microsoft-signed Windows Update driver package",
                "Sentinel will download and install the selected signed driver through Windows Update. It will not restart the computer without your separate approval.");
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
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return line[(name.Length + 1)..].Trim();
                }
            }
            return string.Empty;
        }

        private static string EscapePowerShellLiteral(string value) => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);

        public sealed record DriverRepairPlan(
            bool Available,
            string DeviceName,
            string PackageTitle,
            string Source,
            string TrustStatement,
            string Summary)
        {
            public static DriverRepairPlan Unavailable(string deviceName, string summary) =>
                new(false, deviceName, string.Empty, string.Empty, string.Empty, summary);
        }

        public sealed record DriverRepairResult(
            bool Success,
            bool RestartRequired,
            string Title,
            string Summary);

        private sealed record ProcessResult(bool Success, string Output);
    }
}
