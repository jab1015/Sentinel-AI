/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Reviews active Windows kernel drivers as investigation evidence.
    /// A driver finding is never actionable by itself and must be correlated
    /// with other process, persistence, security, or event evidence.
    /// </summary>
    public sealed class DriverMonitor
    {
        public DriverSnapshot GetSnapshot()
        {
            try
            {
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoLogo -NoProfile -NonInteractive -Command \"Get-CimInstance Win32_SystemDriver | Where-Object {$_.State -eq 'Running'} | Select-Object Name,DisplayName,PathName,StartMode | ConvertTo-Json -Compress\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                if (!process.Start())
                {
                    return Empty("Driver data was unavailable.");
                }

                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(10000) || process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    TryKill(process);
                    return Empty("Driver data was unavailable.");
                }

                using JsonDocument document = JsonDocument.Parse(output);
                IEnumerable<JsonElement> items = document.RootElement.ValueKind == JsonValueKind.Array
                    ? document.RootElement.EnumerateArray()
                    : new[] { document.RootElement };

                List<DriverFinding> findings = new();
                int runningDriverCount = 0;
                int reviewedFileCount = 0;

                foreach (JsonElement item in items)
                {
                    runningDriverCount++;

                    string name = GetString(item, "Name");
                    string displayName = GetString(item, "DisplayName");
                    string rawPath = GetString(item, "PathName");
                    string path = NormalizeDriverPath(rawPath);

                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        continue;
                    }

                    reviewedFileCount++;
                    SignatureAssessment signature = InspectSignature(path);
                    bool systemLocation = IsSystemDriverLocation(path);

                    if (!systemLocation)
                    {
                        findings.Add(new DriverFinding(
                            Fallback(displayName, name),
                            path,
                            $"A running kernel driver is outside the standard Windows driver directory: {Shorten(path)}"));
                    }
                    else if (!signature.IsSigned)
                    {
                        findings.Add(new DriverFinding(
                            Fallback(displayName, name),
                            path,
                            $"A running kernel driver does not expose a readable digital signature: {Shorten(path)}"));
                    }
                    else if (!signature.IsTrusted)
                    {
                        findings.Add(new DriverFinding(
                            Fallback(displayName, name),
                            path,
                            $"A running kernel driver's certificate chain could not be validated. Publisher: {signature.Publisher}."));
                    }
                }

                DriverFinding? primary = findings.Count > 0 ? findings[0] : null;
                return new DriverSnapshot(
                    runningDriverCount,
                    reviewedFileCount,
                    findings.Count,
                    primary?.DriverName ?? "None",
                    primary?.Path ?? "None",
                    primary?.Reason ?? "No unusual running-driver conditions were detected.");
            }
            catch
            {
                return Empty("Driver data was unavailable.");
            }
        }

        private static SignatureAssessment InspectSignature(string path)
        {
            try
            {
                using X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
                using X509Certificate2 certificate2 = new(certificate);
                using X509Chain chain = new();

                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

                bool trusted = chain.Build(certificate2);
                string publisher = certificate2.GetNameInfo(X509NameType.SimpleName, forIssuer: false);

                return new SignatureAssessment(
                    true,
                    trusted,
                    string.IsNullOrWhiteSpace(publisher) ? "Unknown publisher" : publisher);
            }
            catch (CryptographicException)
            {
                return SignatureAssessment.Unsigned;
            }
            catch
            {
                return SignatureAssessment.Unsigned;
            }
        }

        private static string NormalizeDriverPath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return string.Empty;
            }

            string value = rawPath.Trim().Trim('"');
            if (value.StartsWith("\\SystemRoot\\", StringComparison.OrdinalIgnoreCase))
            {
                value = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    value[12..]);
            }
            else if (value.StartsWith("System32\\", StringComparison.OrdinalIgnoreCase))
            {
                value = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    value);
            }

            try
            {
                return Path.GetFullPath(value);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsSystemDriverLocation(string path)
        {
            try
            {
                string driverDirectory = Path.GetFullPath(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32",
                    "drivers"));

                string fullPath = Path.GetFullPath(path);
                return fullPath.StartsWith(
                    driverDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string GetString(JsonElement item, string propertyName)
        {
            return item.TryGetProperty(propertyName, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static string Fallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static string Shorten(string value) =>
            value.Length <= 160 ? value : value[..157] + "...";

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private static DriverSnapshot Empty(string reason) =>
            new(0, 0, 0, "None", "None", reason);

        private sealed record DriverFinding(string DriverName, string Path, string Reason);
        private sealed record SignatureAssessment(bool IsSigned, bool IsTrusted, string Publisher)
        {
            public static SignatureAssessment Unsigned { get; } =
                new(false, false, "Unsigned");
        }

        public sealed record DriverSnapshot(
            int RunningDriverCount,
            int ReviewedDriverFileCount,
            int ReviewFindingCount,
            string PrimaryDriverName,
            string PrimaryDriverPath,
            string PrimaryReason);
    }
}
