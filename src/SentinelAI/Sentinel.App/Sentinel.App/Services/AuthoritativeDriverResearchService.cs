/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sentinel.App.Services
{
    public sealed class AuthoritativeDriverResearchService
    {
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(45);
        private const string DellCatalogUri = "https://downloads.dell.com/catalog/CatalogPC.cab";

        public Task<DriverResearchResult> ResearchAsync(string deviceName) => Task.Run(() => Research(deviceName));

        private static DriverResearchResult Research(string deviceName)
        {
            DeviceContext context = ReadDeviceContext(deviceName);

            if ((context.Manufacturer ?? string.Empty).Contains("Dell", StringComparison.OrdinalIgnoreCase))
            {
                DellPackageMatch? exactDell = TryResolveDellCatalogPackage(context, deviceName);
                if (exactDell is not null)
                {
                    return new DriverResearchResult(
                        true, true, 97, "Dell Driver Catalog", exactDell.DownloadUri,
                        context.Manufacturer, context.Model, context.SerialNumber, context.HardwareId,
                        $"Sentinel identified this computer as {Display(context.Manufacturer)} {Display(context.Model)} and matched the affected device to Dell's official machine-readable driver catalog. Candidate package: {exactDell.Title}. Version: {exactDell.Version}. Release: {exactDell.ReleaseDate}. Sentinel resolved the Dell-hosted package URL. The next safety step is to download the package, verify its Windows digital signature and hardware compatibility, and only then offer automatic installation.",
                        false);
                }
            }

            OemSource oem = BuildOemSource(context, deviceName);
            WebProbe oemProbe = string.IsNullOrWhiteSpace(oem.Uri) ? new(false, string.Empty) : Probe(oem.Uri);

            // Prefer a hardware-ID match from Microsoft's official catalog before falling
            // back to an interactive OEM support page. This gives broad cross-brand coverage.
            string catalogQuery = string.IsNullOrWhiteSpace(context.HardwareId) ? deviceName : context.HardwareId;
            string catalogUri = "https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(catalogQuery);
            WebProbe catalog = Probe(catalogUri);
            if (catalog.Reached && CatalogAppearsToHaveResults(catalog.Body))
            {
                return new DriverResearchResult(
                    true, true, 90, "Microsoft Update Catalog", catalogUri,
                    context.Manufacturer, context.Model, context.SerialNumber, context.HardwareId,
                    "Sentinel found candidate driver information in Microsoft's official Update Catalog using the affected device identity. The exact package must still be matched to this computer's hardware ID and digital signature before Sentinel can install it.",
                    false);
            }

            // If Microsoft Update Catalog cannot resolve the device, use the component
            // vendor only when Sentinel can identify a well-known hardware vendor from
            // verified local device evidence. This remains research-only until an exact
            // signed package is validated.
            OemSource componentVendor = BuildComponentVendorSource(deviceName, context.HardwareId);
            if (!string.IsNullOrWhiteSpace(componentVendor.Uri))
            {
                WebProbe componentProbe = Probe(componentVendor.Uri);
                if (componentProbe.Reached || componentVendor.BrowserAuthoritative)
                {
                    return new DriverResearchResult(
                        true, true, 86, componentVendor.Name, componentVendor.Uri,
                        context.Manufacturer, context.Model, context.SerialNumber, context.HardwareId,
                        $"Sentinel could not verify an exact package through Windows Update or Microsoft Update Catalog. It identified the affected component vendor from verified local evidence and located the vendor's official support source. The computer manufacturer remains preferred for platform-specific drivers, so Sentinel will not install anything until model compatibility and the package signature are verified.",
                        true);
                }
            }

            if (!string.IsNullOrWhiteSpace(oem.Uri) && (oemProbe.Reached || oem.BrowserAuthoritative))
            {
                int confidence = !string.IsNullOrWhiteSpace(context.Model) && !string.IsNullOrWhiteSpace(context.SerialNumber) ? 92 : 84;
                string reachability = oemProbe.Reached
                    ? "Sentinel verified the manufacturer's official support endpoint directly."
                    : "Sentinel identified the manufacturer's official support endpoint, but that interactive site blocks automated access.";
                return new DriverResearchResult(
                    true, true, confidence, oem.Name, oem.Uri,
                    context.Manufacturer, context.Model, context.SerialNumber, context.HardwareId,
                    $"Sentinel identified this computer as {Display(context.Manufacturer)} {Display(context.Model)}. {reachability} Windows Update and Microsoft Update Catalog did not provide an exact automatically installable repair. Sentinel has not verified a signed package, so no installation is allowed.",
                    true);
            }

            string microsoftGuidance = "https://learn.microsoft.com/windows-hardware/drivers/install/cm-prob-failed-start";
            WebProbe guidance = Probe(microsoftGuidance);
            if (guidance.Reached)
            {
                return new DriverResearchResult(
                    true, true, 75, "Microsoft Learn", microsoftGuidance,
                    context.Manufacturer, context.Model, context.SerialNumber, context.HardwareId,
                    "Sentinel verified Microsoft's official guidance for Device Manager Code 10. The device failed to start and the driver requires repair. No exact automatically installable package has been verified yet.",
                    true);
            }

            return new DriverResearchResult(
                false, false, 0, string.Empty, string.Empty,
                context.Manufacturer, context.Model, context.SerialNumber, context.HardwareId,
                "Sentinel could not reach an authoritative Microsoft, computer-manufacturer, or verified component-vendor source. No change was made, and Sentinel will not guess at a repair.",
                true);
        }

        private static DellPackageMatch? TryResolveDellCatalogPackage(DeviceContext context, string deviceName)
        {
            string work = Path.Combine(Path.GetTempPath(), "SentinelAI", "DellCatalog", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(work);
                string cab = Path.Combine(work, "CatalogPC.cab");
                using (HttpClient client = new() { Timeout = NetworkTimeout })
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelAI/1.0");
                    byte[] bytes = client.GetByteArrayAsync(DellCatalogUri).GetAwaiter().GetResult();
                    File.WriteAllBytes(cab, bytes);
                }

                using Process expand = new();
                expand.StartInfo = new ProcessStartInfo
                {
                    FileName = "expand.exe",
                    Arguments = $"\"{cab}\" -F:CatalogPC.xml \"{work}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                if (!expand.Start()) return null;
                _ = expand.StandardOutput.ReadToEnd();
                _ = expand.StandardError.ReadToEnd();
                if (!expand.WaitForExit((int)CommandTimeout.TotalMilliseconds) || expand.ExitCode != 0) return null;

                string xmlPath = Path.Combine(work, "CatalogPC.xml");
                if (!File.Exists(xmlPath)) return null;
                XDocument doc = XDocument.Load(xmlPath, LoadOptions.None);

                string model = Normalize(context.Model);
                string device = Normalize(deviceName);
                string hardware = Normalize(context.HardwareId);
                List<DellPackageMatch> matches = new();

                foreach (XElement component in doc.Descendants().Where(e => e.Name.LocalName.Equals("SoftwareComponent", StringComparison.OrdinalIgnoreCase)))
                {
                    string text = Normalize(component.Value);
                    string xml = Normalize(component.ToString(SaveOptions.DisableFormatting));
                    bool deviceMatch = text.Contains("management engine", StringComparison.OrdinalIgnoreCase) ||
                                       device.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length > 4).Count(x => text.Contains(x, StringComparison.OrdinalIgnoreCase)) >= 2;
                    bool modelMatch = !string.IsNullOrWhiteSpace(model) && xml.Contains(model, StringComparison.OrdinalIgnoreCase);
                    bool hardwareMatch = !string.IsNullOrWhiteSpace(hardware) && xml.Contains(hardware, StringComparison.OrdinalIgnoreCase);
                    if (!deviceMatch || (!modelMatch && !hardwareMatch)) continue;

                    string path = AttributeOrElement(component, "path");
                    if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    string title = AttributeOrElement(component, "Name");
                    if (string.IsNullOrWhiteSpace(title)) title = "Dell driver package";
                    string version = AttributeOrElement(component, "vendorVersion");
                    if (string.IsNullOrWhiteSpace(version)) version = AttributeOrElement(component, "dellVersion");
                    string released = AttributeOrElement(component, "releaseDate");
                    DateTime.TryParse(released, out DateTime releaseDate);
                    string download = "https://downloads.dell.com/" + path.TrimStart('/', '\\').Replace('\\', '/');
                    matches.Add(new DellPackageMatch(title, version, released, download, releaseDate));
                }

                return matches.OrderByDescending(m => m.SortDate).FirstOrDefault();
            }
            catch { return null; }
            finally
            {
                try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
            }
        }

        private static string AttributeOrElement(XElement element, string name)
        {
            XAttribute? attr = element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (attr is not null) return attr.Value.Trim();
            XElement? child = element.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            return child?.Value.Trim() ?? string.Empty;
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

            if (ContainsAny(manufacturer, "Dell", "Alienware")) return new("Dell Support", "https://www.dell.com/support/home/en-us?app=drivers", true);
            if (ContainsAny(manufacturer, "HP", "Hewlett", "Compaq")) return new("HP Support", "https://support.hp.com/us-en/drivers", true);
            if (ContainsAny(manufacturer, "Lenovo")) return new("Lenovo Support", "https://pcsupport.lenovo.com/us/en/", true);
            if (ContainsAny(manufacturer, "ASUS", "ASUSTeK")) return new("ASUS Support", "https://www.asus.com/support/download-center/", true);
            if (ContainsAny(manufacturer, "Acer", "Gateway", "eMachines")) return new("Acer Support", "https://www.acer.com/us-en/support/drivers-and-manuals", true);
            if (ContainsAny(manufacturer, "Microsoft")) return new("Microsoft Surface Support", "https://support.microsoft.com/surface/download-drivers-and-firmware-for-surface-09bb2e09-2a4b-cb69-0951-078a7739e120", true);
            if (ContainsAny(manufacturer, "MSI", "Micro-Star")) return new("MSI Support", "https://www.msi.com/support/download", true);
            if (ContainsAny(manufacturer, "Gigabyte", "GIGABYTE", "AORUS")) return new("GIGABYTE Support", "https://www.gigabyte.com/Support", true);
            if (ContainsAny(manufacturer, "Samsung")) return new("Samsung Support", "https://www.samsung.com/us/support/downloads/", true);
            if (ContainsAny(manufacturer, "TOSHIBA", "Dynabook")) return new("Dynabook Support", "https://support.dynabook.com/support/modelHome", true);
            if (ContainsAny(manufacturer, "Framework")) return new("Framework Support", "https://knowledgebase.frame.work/en_us/framework-laptop-bios-and-driver-releases-S1dMQt6F", true);
            if (ContainsAny(manufacturer, "Razer")) return new("Razer Support", "https://mysupport.razer.com/app/answers/detail/a_id/4166", true);
            if (ContainsAny(manufacturer, "LG Electronics", "LG")) return new("LG Support", "https://www.lg.com/us/support/software-firmware-drivers", true);
            if (ContainsAny(manufacturer, "Huawei")) return new("Huawei Support", "https://consumer.huawei.com/en/support/", true);
            if (ContainsAny(manufacturer, "Fujitsu")) return new("Fujitsu Support", "https://support.ts.fujitsu.com/", true);
            if (ContainsAny(manufacturer, "Panasonic")) return new("Panasonic Toughbook Support", "https://na.panasonic.com/us/support", true);
            if (ContainsAny(manufacturer, "VAIO", "Sony")) return new("VAIO Support", "https://support.us.vaio.com/", true);
            if (ContainsAny(manufacturer, "MEDION")) return new("MEDION Support", "https://www.medion.com/us/service/", true);
            if (ContainsAny(manufacturer, "Intel")) return new("Intel Download Center", "https://www.intel.com/content/www/us/en/download-center/home.html", true);

            return new(string.Empty, string.Empty, false);
        }

        private static OemSource BuildComponentVendorSource(string deviceName, string hardwareId)
        {
            string evidence = (deviceName + " " + hardwareId).Trim();
            if (ContainsAny(evidence, "Intel", "VEN_8086")) return new("Intel Download Center", "https://www.intel.com/content/www/us/en/download-center/home.html", true);
            if (ContainsAny(evidence, "NVIDIA", "VEN_10DE")) return new("NVIDIA Driver Downloads", "https://www.nvidia.com/Download/index.aspx", true);
            if (ContainsAny(evidence, "AMD", "Advanced Micro Devices", "VEN_1002", "VEN_1022")) return new("AMD Drivers and Support", "https://www.amd.com/en/support/download/drivers.html", true);
            if (ContainsAny(evidence, "Realtek", "VEN_10EC")) return new("Realtek Downloads", "https://www.realtek.com/Download/Index", true);
            if (ContainsAny(evidence, "Qualcomm", "Atheros", "VEN_168C", "VEN_17CB")) return new("Qualcomm Support", "https://www.qualcomm.com/support", true);
            if (ContainsAny(evidence, "Broadcom", "VEN_14E4")) return new("Broadcom Support", "https://www.broadcom.com/support/download-search", true);
            return new(string.Empty, string.Empty, false);
        }

        private static bool ContainsAny(string value, params string[] tokens) =>
            tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

        private static WebProbe Probe(string uri)
        {
            try
            {
                using HttpClient client = new() { Timeout = NetworkTimeout };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelAI/1.0");
                using HttpResponseMessage response = client.GetAsync(uri).GetAwaiter().GetResult();
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return new(response.IsSuccessStatusCode, body);
            }
            catch { return new(false, string.Empty); }
        }

        private static bool CatalogAppearsToHaveResults(string body) =>
            !string.IsNullOrWhiteSpace(body) &&
            !body.Contains("We did not find any results", StringComparison.OrdinalIgnoreCase) &&
            (body.Contains("goToDetails", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("updateid", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("ScopedViewInline", StringComparison.OrdinalIgnoreCase));

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
                if (line.TrimStart().StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return line.Trim()[(name.Length + 1)..].Trim();
            return string.Empty;
        }

        private static string EscapePowerShellLiteral(string value) => (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
        private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "this computer" : value.Trim();
        private static string Normalize(string value) => (value ?? string.Empty).Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

        public sealed record DriverResearchResult(bool Completed, bool AuthoritativeSourceReached, int ConfidencePercent, string SourceName, string SourceUri, string Manufacturer, string Model, string SerialNumber, string HardwareId, string Summary, bool UserActionRequired);
        private sealed record DeviceContext(string Manufacturer, string Model, string SerialNumber, string HardwareId);
        private sealed record OemSource(string Name, string Uri, bool BrowserAuthoritative);
        private sealed record WebProbe(bool Reached, string Body);
        private sealed record ProcessResult(bool Success, string Output);
        private sealed record DellPackageMatch(string Title, string Version, string ReleaseDate, string DownloadUri, DateTime SortDate);
    }
}
