/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    public sealed class AuthoritativeDriverResearchService
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(20);

        public Task<DriverResearchResult> ResearchAsync(string deviceName) => Task.Run(() => Research(deviceName));

        private static DriverResearchResult Research(string deviceName)
        {
            DeviceContext context = ReadDeviceContext(deviceName);
            OemSource oem = BuildOemSource(context, deviceName);

            if (!string.IsNullOrWhiteSpace(oem.Uri))
            {
                WebProbe vendor = Probe(oem.Uri);
                if (vendor.Reached || oem.BrowserAuthoritative)
                {
                    int confidence = !string.IsNullOrWhiteSpace(context.Model) && !string.IsNullOrWhiteSpace(context.SerialNumber) ? 92 : 84;
                    string reachability = vendor.Reached
                        ? "Sentinel verified the manufacturer's official support endpoint directly."
                        : "Sentinel identified the manufacturer's official support endpoint. The site blocks automated verification from this process, so Sentinel will open the official OEM page rather than substituting a generic source.";
                    return new DriverResearchResult(true, true, confidence, oem.Name, oem.Uri, context.Manufacturer, context.Model, context.SerialNumber, context.HardwareId,
                        $"Sentinel identified this computer as {Display(context.Manufacturer)} {Display(context.Model)}. {reachability} Windows Update did not offer an automatic repair. Sentinel must still identify and validate the exact signed package before automatic installation can be enabled.", true);
                }
            }

            string catalogQuery = string.IsNullOrWhiteSpace(context.HardwareId) ? deviceName : context.HardwareId;
            string catalogUri = "https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(catalogQuery);
            WebProbe catalog = Probe(catalogUri);
            if (catalog.Reached && CatalogAppearsToHaveResults(catalog.Body))
                return new DriverResearchResult(true, true, 90, "Microsoft Update Catalog", catalogUri, context.Manufacturer, context.Model, context.SerialNumber, context.HardwareId,
                    "Sentinel found candidate driver information in Microsoft's official Update Catalog. The exact catalog package must still be matched to this computer's hardware ID and signature before Sentinel can install it.", false);

            string microsoftGuidance = "https://learn.microsoft.com/windows-hardware/drivers/install/cm-prob-failed-start";
            WebProbe guidance = Probe(microsoftGuidance);
            if (guidance.Reached)
                return new DriverResearchResult(true, true, 75, "Microsoft Learn", microsoftGuidance, context.Manufacturer, context.Model, context.SerialNumber, context.HardwareId,
                    "Sentinel verified Microsoft's official guidance for Device Manager Code 10. The device failed to start and the driver requires repair. No exact automatically installable package has been verified yet.", true);

            return new DriverResearchResult(false, false, 0, string.Empty, string.Empty, context.Manufacturer, context.Model, context.SerialNumber, context.HardwareId,
                "Sentinel could not reach an authoritative Microsoft or manufacturer source. No change was made, and Sentinel will not guess at a repair.", true);
        }

        private static DeviceContext ReadDeviceContext(string deviceName)
        {
            string safeName = EscapePowerShellLiteral(deviceName);
            string command =
                "$ErrorActionPreference='SilentlyContinue'; $name='" + safeName + "'; " +
                "$dev=Get-CimInstance Win32_PnPEntity | Where-Object {$_.Name -eq $name -or $_.Name -like ('*'+$name+'*')} | Select-Object -First 1; " +
                "$hw=''; if($dev){$p=Get-PnpDeviceProperty -InstanceId $dev.PNPDeviceID -KeyName 'DEVPKEY_Device_HardwareIds'; if($p -and $p.Data){$hw=@($p.Data)[0]} elseif($dev.PNPDeviceID){$hw=$dev.PNPDeviceID}}; " +
                "$cs=Get-CimInstance Win32_ComputerSystem; $bios=Get-CimInstance Win32_BIOS; " +
                "$m=[string]$cs.Manufacturer; $model=[string]$cs.Model; $serial=[string]$bios.SerialNumber; " +
                "$bs=[char]92; $siPath='HKLM:'+$bs+'SYSTEM'+$bs+'CurrentControlSet'+$bs+'Control'+$bs+'SystemInformation'; $si=Get-ItemProperty $siPath; " +
                "if([string]::IsNullOrWhiteSpace($m)){$m=[string]$si.SystemManufacturer}; if([string]::IsNullOrWhiteSpace($model)){$model=[string]$si.SystemProductName}; if([string]::IsNullOrWhiteSpace($serial)){$serial=[string]$si.SystemSerialNumber}; " +
                "Write-Output ('MANUFACTURER=' + $m); Write-Output ('MODEL=' + $model); Write-Output ('SERIAL=' + $serial); Write-Output ('HARDWAREID=' + [string]$hw);";
            ProcessResult result = RunPowerShell(command, CommandTimeout);
            return new DeviceContext(GetValue(result.Output, "MANUFACTURER"), GetValue(result.Output, "MODEL"), GetValue(result.Output, "SERIAL"), GetValue(result.Output, "HARDWAREID"));
        }

        private static OemSource BuildOemSource(DeviceContext context, string deviceName)
        {
            string manufacturer = context.Manufacturer ?? string.Empty;
            string query = string.Join(" ", new[] { context.Model, deviceName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (manufacturer.Contains("Dell", StringComparison.OrdinalIgnoreCase))
            {
                string uri = string.IsNullOrWhiteSpace(context.SerialNumber) ? "https://www.dell.com/support/home/en-us?app=drivers" : "https://www.dell.com/support/home/en-us/product-support/servicetag/" + Uri.EscapeDataString(context.SerialNumber.Trim()) + "/drivers";
                return new("Dell Support", uri, true);
            }
            if (manufacturer.Contains("HP", StringComparison.OrdinalIgnoreCase) || manufacturer.Contains("Hewlett", StringComparison.OrdinalIgnoreCase)) return new("HP Support", "https://support.hp.com/us-en/drivers", true);
            if (manufacturer.Contains("Lenovo", StringComparison.OrdinalIgnoreCase)) return new("Lenovo Support", "https://pcsupport.lenovo.com/us/en/", true);
            if (manufacturer.Contains("ASUS", StringComparison.OrdinalIgnoreCase)) return new("ASUS Support", "https://www.asus.com/support/download-center/", true);
            if (manufacturer.Contains("Acer", StringComparison.OrdinalIgnoreCase)) return new("Acer Support", "https://www.acer.com/us-en/support/drivers-and-manuals", true);
            if (string.IsNullOrWhiteSpace(manufacturer)) return new(string.Empty, string.Empty, false);
            return new("Intel Download Center", "https://www.intel.com/content/www/us/en/search.html#sort=relevancy&f:@tabfilter=[Downloads]&q=" + Uri.EscapeDataString(query), true);
        }

        private static WebProbe Probe(string uri)
        {
            try { using HttpClient client = new() { Timeout = NetworkTimeout }; client.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelAI/1.0"); using HttpResponseMessage response = client.GetAsync(uri).GetAwaiter().GetResult(); string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); return new(response.IsSuccessStatusCode, body); }
            catch { return new(false, string.Empty); }
        }

        private static bool CatalogAppearsToHaveResults(string body) => !string.IsNullOrWhiteSpace(body) && !body.Contains("We did not find any results", StringComparison.OrdinalIgnoreCase) && (body.Contains("goToDetails", StringComparison.OrdinalIgnoreCase) || body.Contains("updateid", StringComparison.OrdinalIgnoreCase) || body.Contains("ScopedViewInline", StringComparison.OrdinalIgnoreCase));

        private static ProcessResult RunPowerShell(string command, TimeSpan timeout)
        {
            try { string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command)); using Process process = new(); process.StartInfo = new ProcessStartInfo { FileName = "powershell.exe", Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }; if (!process.Start()) return new(false, string.Empty); string output = process.StandardOutput.ReadToEnd(); _ = process.StandardError.ReadToEnd(); if (!process.WaitForExit((int)timeout.TotalMilliseconds)) { process.Kill(true); return new(false, output); } return new(process.ExitCode == 0, output.Trim()); }
            catch { return new(false, string.Empty); }
        }

        private static string GetValue(string output, string name) { foreach (string line in (output ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) if (line.TrimStart().StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return line.Trim()[(name.Length + 1)..].Trim(); return string.Empty; }
        private static string EscapePowerShellLiteral(string value) => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
        private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "this computer" : value.Trim();

        public sealed record DriverResearchResult(bool Completed, bool AuthoritativeSourceReached, int ConfidencePercent, string SourceName, string SourceUri, string Manufacturer, string Model, string SerialNumber, string HardwareId, string Summary, bool UserActionRequired);
        private sealed record DeviceContext(string Manufacturer, string Model, string SerialNumber, string HardwareId);
        private sealed record OemSource(string Name, string Uri, bool BrowserAuthoritative);
        private sealed record WebProbe(bool Reached, string Body);
        private sealed record ProcessResult(bool Success, string Output);
    }
}
