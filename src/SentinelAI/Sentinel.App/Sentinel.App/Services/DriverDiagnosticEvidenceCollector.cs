using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Collects machine-specific driver evidence locally so Sentinel does not ask
    /// a non-technical user to retrieve Device Manager, BIOS, driver, or event data.
    /// Read-only collection only; this service never changes the system.
    /// </summary>
    public sealed class DriverDiagnosticEvidenceCollector
    {
        public Task<DriverDiagnosticEvidence> CollectAsync(string deviceName) =>
            Task.Run(() => Collect(deviceName));

        private static DriverDiagnosticEvidence Collect(string deviceName)
        {
            string safe = EscapePowerShellLiteral(NormalizeDeviceName(deviceName));
            string command =
                "$name='" + safe + "'; " +
                "$dev=Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {$_.FriendlyName -like \"*$name*\" -or $_.InstanceId -like \"*$name*\"} | Select-Object -First 1; " +
                "if(-not $dev){$dev=Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {$_.FriendlyName -like \"*$name*\"} | Select-Object -First 1}; " +
                "if($dev){ " +
                "\"DEVICE=$($dev.FriendlyName)`nSTATUS=$($dev.Status)`nINSTANCE=$($dev.InstanceId)`nPROBLEM=$($dev.Problem)`n\"; " +
                "$hw=(Get-PnpDeviceProperty -InstanceId $dev.InstanceId -KeyName 'DEVPKEY_Device_HardwareIds' -ErrorAction SilentlyContinue).Data; if($hw){\"HARDWAREIDS=$($hw -join ';')\"}; " +
                "$drv=Get-CimInstance Win32_PnPSignedDriver -ErrorAction SilentlyContinue | Where-Object {$_.DeviceID -eq $dev.InstanceId} | Select-Object -First 1; if($drv){\"DRIVER=$($drv.DriverVersion)|$($drv.DriverDate)|$($drv.InfName)|$($drv.Manufacturer)\"}; " +
                "}; " +
                "$bios=Get-CimInstance Win32_BIOS -ErrorAction SilentlyContinue | Select-Object -First 1; if($bios){\"BIOS=$($bios.SMBIOSBIOSVersion)|$($bios.ReleaseDate)\"}; " +
                "$cs=Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue | Select-Object -First 1; if($cs){\"SYSTEM=$($cs.Manufacturer)|$($cs.Model)\"}; " +
                "$events=Get-WinEvent -FilterHashtable @{LogName='System'; StartTime=(Get-Date).AddDays(-7)} -ErrorAction SilentlyContinue | Where-Object {$_.Message -match 'Management Engine|Intel.*MEI|Code 10'} | Select-Object -First 5; if($events){\"EVENTS=$((@($events | ForEach-Object {\"$($_.TimeCreated): $($_.ProviderName) ID $($_.Id) $($_.LevelDisplayName)\"})) -join ' || ')\"}";

            ProcessResult result = RunPowerShell(command, TimeSpan.FromSeconds(25));
            string output = result.Output;
            return new DriverDiagnosticEvidence(
                GetValue(output, "DEVICE"), GetValue(output, "STATUS"), GetValue(output, "INSTANCE"),
                GetValue(output, "PROBLEM"), GetValue(output, "HARDWAREIDS"), GetValue(output, "DRIVER"),
                GetValue(output, "BIOS"), GetValue(output, "SYSTEM"), GetValue(output, "EVENTS"), result.Success);
        }

        private static string NormalizeDeviceName(string value)
        {
            string name = value ?? string.Empty;
            int code = name.IndexOf("(Code ", StringComparison.OrdinalIgnoreCase);
            if (code >= 0) name = name[..code];
            return name.Trim();
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
            catch { return new(false, string.Empty); }
        }

        private static string GetValue(string output, string name)
        {
            foreach (string line in (output ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return line[(name.Length + 1)..].Trim();
            return string.Empty;
        }

        private static string EscapePowerShellLiteral(string value) => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
        private sealed record ProcessResult(bool Success, string Output);
    }

    public sealed record DriverDiagnosticEvidence(
        string DeviceName, string Status, string InstanceId, string ProblemCode, string HardwareIds,
        string DriverDetails, string BiosDetails, string ComputerIdentity, string RecentSystemEvents, bool CollectionSucceeded)
    {
        public bool HasMachineSpecificEvidence =>
            !string.IsNullOrWhiteSpace(InstanceId) || !string.IsNullOrWhiteSpace(HardwareIds) ||
            !string.IsNullOrWhiteSpace(DriverDetails) || !string.IsNullOrWhiteSpace(BiosDetails);

        public string ToInvestigationSummary()
        {
            StringBuilder b = new();
            if (!string.IsNullOrWhiteSpace(DeviceName)) b.Append("Device: ").Append(DeviceName).Append(". ");
            if (!string.IsNullOrWhiteSpace(Status)) b.Append("Status: ").Append(Status).Append(". ");
            if (!string.IsNullOrWhiteSpace(ProblemCode)) b.Append("Problem: ").Append(ProblemCode).Append(". ");
            if (!string.IsNullOrWhiteSpace(InstanceId)) b.Append("Instance: ").Append(InstanceId).Append(". ");
            if (!string.IsNullOrWhiteSpace(HardwareIds)) b.Append("Hardware IDs: ").Append(HardwareIds).Append(". ");
            if (!string.IsNullOrWhiteSpace(DriverDetails)) b.Append("Installed driver: ").Append(DriverDetails).Append(". ");
            if (!string.IsNullOrWhiteSpace(BiosDetails)) b.Append("BIOS: ").Append(BiosDetails).Append(". ");
            if (!string.IsNullOrWhiteSpace(ComputerIdentity)) b.Append("Computer: ").Append(ComputerIdentity).Append(". ");
            if (!string.IsNullOrWhiteSpace(RecentSystemEvents)) b.Append("Recent relevant events: ").Append(RecentSystemEvents).Append(". ");
            return b.ToString().Trim();
        }
    }
}
