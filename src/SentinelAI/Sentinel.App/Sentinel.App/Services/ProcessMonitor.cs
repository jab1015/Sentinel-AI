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

namespace Sentinel.App.Services
{
    public class ProcessMonitor
    {
        private static readonly string[] TrustedPublisherNames =
        {
            "Microsoft Corporation", "Google LLC", "Google Inc", "GitHub, Inc.",
            "JetBrains s.r.o.", "Docker Inc", "NVIDIA Corporation", "Intel Corporation",
            "Advanced Micro Devices, Inc.", "Adobe Inc.", "Oracle America, Inc.", "Mozilla Corporation",
            "VMware, Inc.", "Broadcom Inc."
        };

        private readonly Dictionary<string, SignatureAssessment> _signatureCache = new(StringComparer.OrdinalIgnoreCase);

        public ProcessIntelligenceSnapshot GetIntelligence()
        {
            List<ProcessFinding> findings = new();
            Process[] processes = Process.GetProcesses();
            string highestMemoryProcessName = "Unknown";
            long highestWorkingSet = 0;

            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        string processName = process.ProcessName;
                        long workingSet = process.WorkingSet64;
                        string path = GetProcessPath(process);
                        SignatureAssessment signature = GetSignatureAssessment(path);
                        string productName = GetProductName(path);

                        if (workingSet > highestWorkingSet)
                        {
                            highestWorkingSet = workingSet;
                            highestMemoryProcessName = processName;
                        }

                        if (!IsUserWritableLocation(path)) continue;

                        bool temporaryLocation = IsTemporaryLocation(path);
                        if (IsKnownTrustedConsoleComponent(processName, path, signature)) continue;

                        if (temporaryLocation)
                        {
                            string signer = signature.IsSigned ? $" Signed by {signature.Publisher}." : string.Empty;
                            findings.Add(new ProcessFinding(processName, $"Running from a temporary location: {ShortenPath(path)}.{signer}", process.Id, GetStartTimeUtc(process)));
                        }
                        else if (!signature.IsSigned)
                        {
                            findings.Add(new ProcessFinding(processName, $"Unsigned executable in a user-writable location: {ShortenPath(path)}", process.Id, GetStartTimeUtc(process)));
                        }
                        else if (!signature.IsTrustedPublisher)
                        {
                            findings.Add(new ProcessFinding(processName, $"Signed by {signature.Publisher}, but running from a user-writable location: {ShortenPath(path)}", process.Id, GetStartTimeUtc(process)));
                        }
                    }
                    catch
                    {
                        // Protected and exited processes are skipped safely.
                    }
                }

                ProcessFinding? primary = findings.Count > 0 ? findings[0] : null;
                return new ProcessIntelligenceSnapshot(
                    processes.Length,
                    highestMemoryProcessName,
                    Math.Round(highestWorkingSet / 1024d / 1024d / 1024d, 2),
                    findings.Count,
                    primary?.ProcessName ?? "None",
                    primary?.Reason ?? "No process warning conditions were detected.",
                    PrimaryProcessId: primary?.ProcessId ?? 0,
                    PrimaryProcessStartUtc: primary?.StartTimeUtc);
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }

        private static string BuildHighMemoryReason(string processName, long workingSet, string path, SignatureAssessment signature, string productName)
        {
            string memory = $"{workingSet / 1024d / 1024d / 1024d:0.00} GB";
            string identity = string.IsNullOrWhiteSpace(productName) ? processName : productName;
            string location = string.IsNullOrWhiteSpace(path) ? "Windows did not expose the executable path." : $"Location: {ShortenPath(path)}.";
            string publisher = signature.IsSigned
                ? $"Publisher/signature: {signature.Publisher}{(signature.IsTrusted ? " (signature chain verified)" : " (signature present; trust could not be fully verified)")}."
                : "Publisher/signature: no verifiable digital signature was found.";

            if (IsVmwareVirtualMachineProcess(processName, productName, signature.Publisher))
            {
                return $"{identity} is the VMware virtual-machine process and is using {memory} of memory. {publisher} {location} This level of memory use can be expected while a virtual machine is running. No action is required unless the virtual machine is causing performance problems.";
            }

            return $"{identity} is using {memory} of memory. {publisher} {location} High memory use alone is a performance observation, not evidence of malware. Review only if the application is unexpected or the computer is experiencing performance problems.";
        }

        private static bool IsVmwareVirtualMachineProcess(string processName, string productName, string publisher) =>
            processName.Equals("vmware-vmx", StringComparison.OrdinalIgnoreCase) ||
            productName.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
            publisher.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
            publisher.Contains("Broadcom", StringComparison.OrdinalIgnoreCase);

        private static string GetProductName(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return string.Empty;
            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
                return version.ProductName?.Trim() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static bool IsWindowsMemoryCompression(string processName) =>
            processName.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("MemoryCompression", StringComparison.OrdinalIgnoreCase);

        private SignatureAssessment GetSignatureAssessment(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return SignatureAssessment.Unsigned;
            DateTime lastWriteTimeUtc;
            try { lastWriteTimeUtc = File.GetLastWriteTimeUtc(path); }
            catch { return SignatureAssessment.Unsigned; }
            string cacheKey = $"{path}|{lastWriteTimeUtc.Ticks}";
            if (_signatureCache.TryGetValue(cacheKey, out SignatureAssessment? cached)) return cached;
            SignatureAssessment assessment = InspectSignature(path);
            if (_signatureCache.Count >= 500) _signatureCache.Clear();
            _signatureCache[cacheKey] = assessment;
            return assessment;
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
                bool chainTrusted = chain.Build(certificate2);
                string publisher = GetPublisherName(certificate2);
                return new SignatureAssessment(true, chainTrusted, IsTrustedPublisher(publisher), publisher);
            }
            catch (CryptographicException) { return SignatureAssessment.Unsigned; }
            catch { return SignatureAssessment.Unsigned; }
        }

        private static bool IsKnownTrustedConsoleComponent(string processName, string path, SignatureAssessment signature)
        {
            if (!processName.Equals("OpenConsole", StringComparison.OrdinalIgnoreCase) || !signature.IsSigned ||
                !signature.Publisher.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase)) return false;
            string normalizedPath = path.Replace('/', '\\');
            return normalizedPath.Contains("\\node_modules.asar.unpacked\\node-pty\\", StringComparison.OrdinalIgnoreCase) &&
                   normalizedPath.Contains("\\conpty\\OpenConsole.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPublisherName(X509Certificate2 certificate)
        {
            string simpleName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            return string.IsNullOrWhiteSpace(simpleName) ? "an unknown publisher" : simpleName;
        }

        private static bool IsTrustedPublisher(string publisher)
        {
            foreach (string trustedPublisher in TrustedPublisherNames)
                if (publisher.Contains(trustedPublisher, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string GetProcessPath(Process process)
        {
            try { return process.MainModule?.FileName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool IsUserWritableLocation(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string temp = Path.GetFullPath(Path.GetTempPath());
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloads = Path.Combine(userProfile, "Downloads");
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return IsWithinDirectory(fullPath, temp) ||
                       IsWithinDirectory(fullPath, downloads) ||
                       IsWithinDirectory(fullPath, appData) ||
                       IsWithinDirectory(fullPath, localAppData);
            }
            catch { return false; }
        }

        private static bool IsTemporaryLocation(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string temp = Path.GetFullPath(Path.GetTempPath());
                return IsWithinDirectory(fullPath, temp);
            }
            catch { return false; }
        }

        private static bool IsWithinDirectory(string fullPath, string directory)
        {
            string normalizedDirectory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                         Path.DirectorySeparatorChar;
            return fullPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static DateTimeOffset? GetStartTimeUtc(Process process)
        {
            try { return process.StartTime.ToUniversalTime(); }
            catch { return null; }
        }

        private static string ShortenPath(string path) => path.Length <= 90 ? path : "..." + path[^87..];
        private sealed record ProcessFinding(
            string ProcessName,
            string Reason,
            int ProcessId,
            DateTimeOffset? StartTimeUtc);
        private sealed record SignatureAssessment(bool IsSigned, bool IsTrusted, bool IsTrustedPublisher, string Publisher)
        {
            public static SignatureAssessment Unsigned { get; } = new(false, false, false, "Unsigned");
        }

        public sealed record ProcessIntelligenceSnapshot(
            int TotalProcessCount, string HighestMemoryProcessName, double HighestMemoryProcessGB,
            int FlaggedProcessCount, string PrimaryProcessName, string PrimaryReason,
            bool CollectionAvailable = true,
            int PrimaryProcessId = 0,
            DateTimeOffset? PrimaryProcessStartUtc = null)
        {
            public static ProcessIntelligenceSnapshot Unavailable { get; } =
                new(0, "Unavailable", 0, 0, "Unavailable", "Process evidence could not be collected.", false);
        }
    }
}
