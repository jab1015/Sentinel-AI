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
            "Microsoft Corporation",
            "Google LLC",
            "Google Inc",
            "GitHub, Inc.",
            "JetBrains s.r.o.",
            "Docker Inc",
            "NVIDIA Corporation",
            "Intel Corporation",
            "Advanced Micro Devices, Inc.",
            "Adobe Inc.",
            "Oracle America, Inc.",
            "Mozilla Corporation"
        };

        private readonly Dictionary<string, SignatureAssessment> _signatureCache =
            new(StringComparer.OrdinalIgnoreCase);

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

                        if (workingSet > highestWorkingSet)
                        {
                            highestWorkingSet = workingSet;
                            highestMemoryProcessName = processName;
                        }

                        if (workingSet >= 2L * 1024 * 1024 * 1024)
                        {
                            findings.Add(new ProcessFinding(
                                processName,
                                $"High memory use ({workingSet / 1024d / 1024d / 1024d:0.00} GB)"));
                        }

                        string path = GetProcessPath(process);
                        if (!IsUserWritableLocation(path))
                        {
                            continue;
                        }

                        SignatureAssessment signature = GetSignatureAssessment(path);
                        bool temporaryLocation = IsTemporaryLocation(path);

                        if (IsKnownTrustedConsoleComponent(processName, path, signature))
                        {
                            continue;
                        }

                        if (temporaryLocation)
                        {
                            string signer = signature.IsSigned
                                ? $" Signed by {signature.Publisher}."
                                : string.Empty;

                            findings.Add(new ProcessFinding(
                                processName,
                                $"Running from a temporary location: {ShortenPath(path)}.{signer}"));
                        }
                        else if (!signature.IsSigned)
                        {
                            findings.Add(new ProcessFinding(
                                processName,
                                $"Unsigned executable in a user-writable location: {ShortenPath(path)}"));
                        }
                        else if (!signature.IsTrustedPublisher)
                        {
                            findings.Add(new ProcessFinding(
                                processName,
                                $"Signed by {signature.Publisher}, but running from a user-writable location: {ShortenPath(path)}"));
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
                    primary?.Reason ?? "No process warning conditions were detected.");
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }

        private SignatureAssessment GetSignatureAssessment(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return SignatureAssessment.Unsigned;
            }

            DateTime lastWriteTimeUtc;
            try
            {
                lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return SignatureAssessment.Unsigned;
            }

            string cacheKey = $"{path}|{lastWriteTimeUtc.Ticks}";
            if (_signatureCache.TryGetValue(cacheKey, out SignatureAssessment? cached))
            {
                return cached;
            }

            SignatureAssessment assessment = InspectSignature(path);

            if (_signatureCache.Count >= 500)
            {
                _signatureCache.Clear();
            }

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
                bool trustedPublisher = IsTrustedPublisher(publisher);

                return new SignatureAssessment(
                    IsSigned: true,
                    IsTrusted: chainTrusted,
                    IsTrustedPublisher: trustedPublisher,
                    Publisher: publisher);
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

        private static bool IsKnownTrustedConsoleComponent(
            string processName,
            string path,
            SignatureAssessment signature)
        {
            if (!processName.Equals("OpenConsole", StringComparison.OrdinalIgnoreCase) ||
                !signature.IsSigned ||
                !signature.Publisher.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalizedPath = path.Replace('/', '\\');
            return normalizedPath.Contains("\\node_modules.asar.unpacked\\node-pty\\", StringComparison.OrdinalIgnoreCase) &&
                   normalizedPath.Contains("\\conpty\\OpenConsole.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPublisherName(X509Certificate2 certificate)
        {
            string simpleName = certificate.GetNameInfo(
                X509NameType.SimpleName,
                forIssuer: false);

            return string.IsNullOrWhiteSpace(simpleName)
                ? "an unknown publisher"
                : simpleName;
        }

        private static bool IsTrustedPublisher(string publisher)
        {
            foreach (string trustedPublisher in TrustedPublisherNames)
            {
                if (publisher.Contains(trustedPublisher, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetProcessPath(Process process)
        {
            try { return process.MainModule?.FileName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool IsUserWritableLocation(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                string temp = Path.GetFullPath(Path.GetTempPath());
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloads = Path.Combine(userProfile, "Downloads");
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                return fullPath.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
                       fullPath.StartsWith(downloads, StringComparison.OrdinalIgnoreCase) ||
                       fullPath.StartsWith(appData, StringComparison.OrdinalIgnoreCase) ||
                       fullPath.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTemporaryLocation(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string temp = Path.GetFullPath(Path.GetTempPath());
                return fullPath.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ShortenPath(string path) =>
            path.Length <= 90 ? path : "..." + path[^87..];

        private sealed record ProcessFinding(string ProcessName, string Reason);

        private sealed record SignatureAssessment(
            bool IsSigned,
            bool IsTrusted,
            bool IsTrustedPublisher,
            string Publisher)
        {
            public static SignatureAssessment Unsigned { get; } =
                new(false, false, false, "Unsigned");
        }

        public sealed record ProcessIntelligenceSnapshot(
            int TotalProcessCount,
            string HighestMemoryProcessName,
            double HighestMemoryProcessGB,
            int FlaggedProcessCount,
            string PrimaryProcessName,
            string PrimaryReason);
    }
}
