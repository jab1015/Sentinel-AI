/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sentinel.App.Services
{
    internal enum AuthenticodeTrustStatus
    {
        Trusted,
        TrustedTimestamped,
        Unsigned,
        InvalidSignature,
        ModifiedAfterSigning,
        UntrustedSigner,
        RevokedSigner,
        ExpiredSignature,
        VerificationError,
        FileChangedDuringVerification
    }

    internal sealed record AuthenticodeTrustResult(
        AuthenticodeTrustStatus Status,
        string Publisher,
        string Sha256,
        int NativeStatusCode,
        string Explanation)
    {
        public bool IsSigned => Status is not AuthenticodeTrustStatus.Unsigned;
        public bool IsTrusted => Status is AuthenticodeTrustStatus.Trusted or AuthenticodeTrustStatus.TrustedTimestamped;
    }

    internal static class AuthenticodeTrustService
    {
        private const uint WtdUiNone = 2;
        private const uint WtdRevokeWholeChain = 1;
        private const uint WtdChoiceFile = 1;
        private const uint WtdStateActionVerify = 1;
        private const uint WtdStateActionClose = 2;
        private const uint WtdProvFlagsRevocationCheckChainExcludeRoot = 0x00000080;

        private const int TrustENoSignature = unchecked((int)0x800B0100);
        private const int TrustEBadDigest = unchecked((int)0x80096010);
        private const int TrustESubjectNotTrusted = unchecked((int)0x800B0004);
        private const int TrustEExplicitDistrust = unchecked((int)0x800B0111);
        private const int CertEUntrustedRoot = unchecked((int)0x800B0109);
        private const int CertEChaining = unchecked((int)0x800B010A);
        private const int CertERevoked = unchecked((int)0x800B010C);
        private const int CertEExpired = unchecked((int)0x800B0101);

        private static readonly Guid WintrustActionGenericVerifyV2 =
            new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        public static AuthenticodeTrustResult Verify(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new(AuthenticodeTrustStatus.VerificationError, "Unknown", string.Empty, 0,
                    "The executable could not be opened for signature verification.");
            }

            string beforeHash;
            try
            {
                beforeHash = ComputeSha256(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                return new(AuthenticodeTrustStatus.VerificationError, "Unknown", string.Empty, ex.HResult,
                    "The executable could not be read completely for signature verification.");
            }

            string publisher = TryGetPublisher(path, out DateTime notAfterUtc);
            int nativeStatus = VerifyWithWinTrust(path);

            string afterHash;
            try
            {
                afterHash = ComputeSha256(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                return new(AuthenticodeTrustStatus.VerificationError, publisher, beforeHash, ex.HResult,
                    "The executable changed or became unreadable while its signature was being verified.");
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(beforeHash), Convert.FromHexString(afterHash)))
            {
                return new(AuthenticodeTrustStatus.FileChangedDuringVerification, publisher, afterHash, nativeStatus,
                    "The executable changed while Sentinel was verifying it, so no trust decision was made.");
            }

            AuthenticodeTrustStatus status = MapStatus(nativeStatus, notAfterUtc);
            return new(status, publisher, afterHash, nativeStatus, Explain(status, publisher));
        }

        internal static AuthenticodeTrustStatus MapStatus(int nativeStatus, DateTime signerNotAfterUtc)
        {
            if (nativeStatus == 0)
            {
                return signerNotAfterUtc != DateTime.MinValue && signerNotAfterUtc < DateTime.UtcNow
                    ? AuthenticodeTrustStatus.TrustedTimestamped
                    : AuthenticodeTrustStatus.Trusted;
            }

            return nativeStatus switch
            {
                TrustENoSignature => AuthenticodeTrustStatus.Unsigned,
                TrustEBadDigest => AuthenticodeTrustStatus.ModifiedAfterSigning,
                CertERevoked => AuthenticodeTrustStatus.RevokedSigner,
                CertEExpired => AuthenticodeTrustStatus.ExpiredSignature,
                CertEUntrustedRoot or CertEChaining or TrustEExplicitDistrust => AuthenticodeTrustStatus.UntrustedSigner,
                TrustESubjectNotTrusted => AuthenticodeTrustStatus.InvalidSignature,
                _ => AuthenticodeTrustStatus.VerificationError
            };
        }

        private static string Explain(AuthenticodeTrustStatus status, string publisher) => status switch
        {
            AuthenticodeTrustStatus.Trusted => $"The Authenticode signature is valid and trusted. Signer: {publisher}.",
            AuthenticodeTrustStatus.TrustedTimestamped => $"The Authenticode signature is valid through a trusted timestamp. Signer: {publisher}.",
            AuthenticodeTrustStatus.Unsigned => "No Authenticode signature is present. Unsigned software is not automatically malicious.",
            AuthenticodeTrustStatus.ModifiedAfterSigning => "The file's Authenticode digest no longer matches the signed content.",
            AuthenticodeTrustStatus.UntrustedSigner => $"A signature is present, but Windows does not trust the signer or certificate chain. Signer: {publisher}.",
            AuthenticodeTrustStatus.RevokedSigner => $"Windows reports that the signing certificate has been revoked. Signer: {publisher}.",
            AuthenticodeTrustStatus.ExpiredSignature => $"The signature is expired and Windows did not validate a trusted timestamp. Signer: {publisher}.",
            AuthenticodeTrustStatus.InvalidSignature => "Windows rejected the executable's Authenticode signature.",
            AuthenticodeTrustStatus.FileChangedDuringVerification => "The file changed while verification was in progress.",
            _ => "Windows could not establish the executable's Authenticode trust state."
        };

        private static string TryGetPublisher(string path, out DateTime notAfterUtc)
        {
            notAfterUtc = DateTime.MinValue;
            try
            {
                using X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
                using X509Certificate2 certificate2 = new(certificate);
                notAfterUtc = certificate2.NotAfter.ToUniversalTime();
                string simpleName = certificate2.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
                return string.IsNullOrWhiteSpace(simpleName) ? "Unknown signer" : simpleName.Trim();
            }
            catch (CryptographicException)
            {
                return "Unsigned";
            }
            catch
            {
                return "Unknown signer";
            }
        }

        private static string ComputeSha256(string path)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static int VerifyWithWinTrust(string path)
        {
            IntPtr filePathPtr = IntPtr.Zero;
            IntPtr fileInfoPtr = IntPtr.Zero;
            try
            {
                filePathPtr = Marshal.StringToCoTaskMemUni(path);
                WinTrustFileInfo fileInfo = new()
                {
                    cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                    pcwszFilePath = filePathPtr,
                    hFile = IntPtr.Zero,
                    pgKnownSubject = IntPtr.Zero
                };

                fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

                WinTrustData data = CreateTrustData(fileInfoPtr, WtdStateActionVerify);
                int result = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, ref data);

                WinTrustData closeData = data;
                closeData.dwStateAction = WtdStateActionClose;
                _ = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, ref closeData);
                return result;
            }
            finally
            {
                if (fileInfoPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfoPtr);
                if (filePathPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(filePathPtr);
            }
        }

        private static WinTrustData CreateTrustData(IntPtr fileInfoPtr, uint stateAction) => new()
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
            pPolicyCallbackData = IntPtr.Zero,
            pSIPClientData = IntPtr.Zero,
            dwUIChoice = WtdUiNone,
            fdwRevocationChecks = WtdRevokeWholeChain,
            dwUnionChoice = WtdChoiceFile,
            pFile = fileInfoPtr,
            dwStateAction = stateAction,
            hWVTStateData = IntPtr.Zero,
            pwszURLReference = IntPtr.Zero,
            dwProvFlags = WtdProvFlagsRevocationCheckChainExcludeRoot,
            dwUIContext = 0,
            pSignatureSettings = IntPtr.Zero
        };

        [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WinTrustData pWVTData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }
    }
}
