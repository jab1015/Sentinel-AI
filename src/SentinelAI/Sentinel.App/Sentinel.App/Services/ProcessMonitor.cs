/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

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

                        if (workingSet > highestWorkingSet)
                        {
                            highestWorkingSet = workingSet;
                            highestMemoryProcessName = processName;
                        }

                        // Signature verification is comparatively expensive and is only security-relevant
                        // here for executables in locations an ordinary user can typically modify.
                        if (!IsUserWritableLocation(path)) continue;

                        SignatureAssessment signature = GetSignatureAssessment(path);
                        bool temporaryLocation = IsTemporaryLocation(path);
                        if (IsKnownTrustedConsoleComponent(processName, path, signature)) continue;

                        if (temporaryLocation)
                        {
                            findings.Add(new ProcessFinding(
                                processName,
                                BuildTemporaryLocationReason(path, signature),
                                process.Id,
                                GetStartTimeUtc(process)));
                        }
                        else if (signature.Status == AuthenticodeTrustStatus.Unsigned)
                        {
                            findings.Add(new ProcessFinding(processName, $"Unsigned executable in a user-writable location: {ShortenPath(path)}", process.Id, GetStartTimeUtc(process)));
                        }
                        else if (!signature.IsTrusted)
                        {
                            findings.Add(new ProcessFinding(
                                processName,
                                $"Executable in a user-writable location has an untrusted Authenticode result ({DescribeStatus(signature.Status)}). Publisher: {signature.Publisher}. Location: {ShortenPath(path)}. {signature.Explanation}",
                                process.Id,
                                GetStartTimeUtc(process)));
                        }
                        else if (!signature.IsTrustedPublisher)
                        {
                            findings.Add(new ProcessFinding(processName, $"Windows verified the executable signature, but the signer ({signature.Publisher}) is not on Sentinel's recognized-publisher list. Location: {ShortenPath(path)}", process.Id, GetStartTimeUtc(process)));
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

        private static string BuildTemporaryLocationReason(string path, SignatureAssessment signature)
        {
            string signatureText = signature.Status switch
            {
                AuthenticodeTrustStatus.Trusted => $" Windows verified the Authenticode signature from {signature.Publisher}.",
                AuthenticodeTrustStatus.TrustedTimestamped => $" Windows verified the timestamped Authenticode signature from {signature.Publisher}.",
                AuthenticodeTrustStatus.Unsigned => " No Authenticode signature was found.",
                _ => $" Authenticode result: {DescribeStatus(signature.Status)}. Publisher: {signature.Publisher}. {signature.Explanation}"
            };
            return $"Running from a temporary location: {ShortenPath(path)}.{signatureText}";
        }

        private SignatureAssessment GetSignatureAssessment(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return SignatureAssessment.VerificationError;

            FileInfo fileInfo;
            try { fileInfo = new FileInfo(path); }
            catch { return SignatureAssessment.VerificationError; }

            // Bind cached trust to content, not just a path/mtime tuple. This avoids accepting a
            // replacement file that preserves its timestamp. Only user-writable candidates reach here.
            string contentHash;
            try { contentHash = ComputeSha256(path); }
            catch { return SignatureAssessment.VerificationError; }

            string cacheKey = $"{path}|{fileInfo.Length}|{contentHash}";
            if (_signatureCache.TryGetValue(cacheKey, out SignatureAssessment? cached)) return cached;

            AuthenticodeVerificationResult verification = AuthenticodeVerifier.Verify(path);
            SignatureAssessment assessment = new(
                verification.Status,
                verification.IsSigned,
                verification.IsTrusted,
                verification.IsTrusted && IsTrustedPublisher(verification.Publisher),
                verification.Publisher,
                verification.Explanation);

            if (_signatureCache.Count >= 500) _signatureCache.Clear();
            _signatureCache[cacheKey] = assessment;
            return assessment;
        }

        private static string ComputeSha256(string path)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] digest = SHA256.HashData(stream);
            return Convert.ToHexString(digest);
        }

        private static bool IsKnownTrustedConsoleComponent(string processName, string path, SignatureAssessment signature)
        {
            if (!processName.Equals("OpenConsole", StringComparison.OrdinalIgnoreCase) ||
                !signature.IsTrusted ||
                !string.Equals(signature.Publisher, "Microsoft Corporation", StringComparison.OrdinalIgnoreCase))
                return false;

            string normalizedPath = path.Replace('/', '\\');
            return normalizedPath.Contains("\\node_modules.asar.unpacked\\node-pty\\", StringComparison.OrdinalIgnoreCase) &&
                   normalizedPath.Contains("\\conpty\\OpenConsole.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTrustedPublisher(string publisher)
        {
            foreach (string trustedPublisher in TrustedPublisherNames)
                if (string.Equals(publisher, trustedPublisher, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string DescribeStatus(AuthenticodeTrustStatus status) => status switch
        {
            AuthenticodeTrustStatus.Trusted => "trusted signature",
            AuthenticodeTrustStatus.TrustedTimestamped => "trusted timestamped signature",
            AuthenticodeTrustStatus.Unsigned => "unsigned",
            AuthenticodeTrustStatus.InvalidSignature => "invalid signature",
            AuthenticodeTrustStatus.ModifiedAfterSigning => "modified after signing",
            AuthenticodeTrustStatus.UntrustedSigner => "untrusted signer",
            AuthenticodeTrustStatus.Revoked => "revoked signer",
            AuthenticodeTrustStatus.Expired => "expired signature",
            AuthenticodeTrustStatus.ExplicitlyDistrusted => "explicitly distrusted signature",
            _ => "verification error"
        };

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

        private sealed record SignatureAssessment(
            AuthenticodeTrustStatus Status,
            bool IsSigned,
            bool IsTrusted,
            bool IsTrustedPublisher,
            string Publisher,
            string Explanation)
        {
            public static SignatureAssessment VerificationError { get; } = new(
                AuthenticodeTrustStatus.VerificationError,
                false,
                false,
                false,
                "Unknown",
                "The executable could not be verified.");
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
