/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Collects local Windows driver and Plug-and-Play evidence without guessing.
    /// </summary>
    public sealed class DriverHealthEvidenceProvider
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

        public DriverHealthSnapshot GetSnapshot()
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
                return DriverHealthSnapshot.Unavailable();

            DriverEvidence evidence = Parse(value);
            string primaryProblem = string.Empty;
            if (!string.IsNullOrWhiteSpace(evidence.Problems))
            {
                string[] problems = evidence.Problems.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (problems.Length > 0) primaryProblem = problems[0];
            }

            string deviceName = primaryProblem;
            int codeIndex = deviceName.LastIndexOf("(Code ", StringComparison.OrdinalIgnoreCase);
            if (codeIndex > 0) deviceName = deviceName[..codeIndex].Trim();

            return new DriverHealthSnapshot(
                Available: true,
                DevicesChecked: evidence.Devices,
                ProblemDeviceCount: evidence.ProblemDevices,
                UnsignedDriverCount: evidence.UnsignedDrivers,
                RecentDriverEventCount: evidence.RecentDriverEvents,
                PrimaryProblem: primaryProblem,
                PrimaryDeviceName: deviceName,
                RequiresAttention: evidence.ProblemDevices > 0);
        }

        public string GetDriverHealthStatus()
        {
            DriverHealthSnapshot snapshot = GetSnapshot();
            if (!snapshot.Available)
            {
                return "Driver health\n\nSentinel could not verify driver health because Windows did not expose Plug-and-Play driver evidence to this process.\n\nRecommended action\nNo change will be made. Run Sentinel with normal Windows access and try again.";
            }

            return BuildUserFacingResponse(snapshot);
        }

        private static string BuildUserFacingResponse(DriverHealthSnapshot evidence)
        {
            var response = new StringBuilder();
            response.AppendLine("Driver health");
            response.AppendLine();

            if (!evidence.RequiresAttention)
            {
                response.AppendLine("Your drivers look healthy.");
                response.AppendLine($"Sentinel checked {evidence.DevicesChecked} devices and Windows reported no device-driver conflicts.");
                response.AppendLine();
                response.AppendLine("Recommended action");
                response.Append("No action is required. Sentinel will continue monitoring.");
                return response.ToString();
            }

            response.AppendLine("A driver needs attention.");
            response.AppendLine($"Sentinel checked {evidence.DevicesChecked} devices. Windows reported {evidence.ProblemDeviceCount} device{(evidence.ProblemDeviceCount == 1 ? string.Empty : "s")} that may not be working correctly.");
            response.AppendLine();
            response.AppendLine("What I found");
            response.AppendLine(string.IsNullOrWhiteSpace(evidence.PrimaryProblem)
                ? "Windows reported a driver problem, but did not provide a device name."
                : evidence.PrimaryProblem);
            response.AppendLine();
            response.AppendLine("What this means");
            response.AppendLine(evidence.RecentDriverEventCount == 0
                ? "No recent driver-related failures were found in the Windows System log. The device status still needs review."
                : $"Windows recorded {evidence.RecentDriverEventCount} recent driver-related event{(evidence.RecentDriverEventCount == 1 ? string.Empty : "s")}, so Sentinel should investigate before making a change.");
            response.AppendLine();
            response.AppendLine("Recommended action");
            response.AppendLine("Sentinel should identify the correct signed replacement driver, verify its source and compatibility, and prepare a repair for your approval. If the repair requires a restart, Sentinel should wait until you confirm that your work is saved before restarting the computer.");
            response.AppendLine();
            response.Append("Technical evidence: ");
            response.Append($"{evidence.DevicesChecked} devices checked; {evidence.ProblemDeviceCount} problem device(s); {evidence.UnsignedDriverCount} unsigned-driver record(s); {evidence.RecentDriverEventCount} recent driver event(s).");
            return response.ToString();
        }

        private static DriverEvidence Parse(string value)
        {
            int devices = 0;
            int problemDevices = 0;
            int unsignedDrivers = 0;
            int recentDriverEvents = 0;
            string problems = string.Empty;
            string events = string.Empty;

            foreach (string segment in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int separator = segment.IndexOf('=');
                if (separator <= 0) continue;

                string key = segment[..separator].Trim();
                string itemValue = segment[(separator + 1)..].Trim();
                switch (key)
                {
                    case "Devices": int.TryParse(itemValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out devices); break;
                    case "ProblemDevices": int.TryParse(itemValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out problemDevices); break;
                    case "UnsignedDrivers": int.TryParse(itemValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out unsignedDrivers); break;
                    case "RecentDriverEvents": int.TryParse(itemValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out recentDriverEvents); break;
                    case "Problems": problems = itemValue; break;
                    case "Events": events = itemValue; break;
                }
            }

            return new DriverEvidence(devices, problemDevices, unsignedDrivers, recentDriverEvents, problems, events);
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

        public sealed record DriverHealthSnapshot(
            bool Available,
            int DevicesChecked,
            int ProblemDeviceCount,
            int UnsignedDriverCount,
            int RecentDriverEventCount,
            string PrimaryProblem,
            string PrimaryDeviceName,
            bool RequiresAttention)
        {
            public static DriverHealthSnapshot Unavailable() =>
                new(false, 0, 0, 0, 0, string.Empty, string.Empty, false);
        }

        private sealed record DriverEvidence(
            int Devices,
            int ProblemDevices,
            int UnsignedDrivers,
            int RecentDriverEvents,
            string Problems,
            string Events);
    }
}
