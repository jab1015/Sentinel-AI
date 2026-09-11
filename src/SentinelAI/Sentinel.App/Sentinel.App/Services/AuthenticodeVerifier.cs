/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sentinel.App.Services;

internal static class AuthenticodeVerifier
{
    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    internal static AuthenticodeVerificationResult Verify(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new(AuthenticodeTrustStatus.VerificationError, false, false, "Unknown", "The executable could not be opened for signature verification.");

        string beforeHash;
        try
        {
            beforeHash = ComputeSha256(path);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return new(AuthenticodeTrustStatus.VerificationError, false, false, "Unknown", "The executable could not be read completely for signature verification.");
        }

        SignerMetadata signer = TryGetSigner(path);
        IntPtr fileInfoPointer = IntPtr.Zero;
        IntPtr pathPointer = IntPtr.Zero;
        AuthenticodeVerificationResult result;

        try
        {
            pathPointer = Marshal.StringToCoTaskMemUni(path);
            WinTrustFileInfo fileInfo = new()
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = pathPointer,
                FileHandle = IntPtr.Zero,
                KnownSubject = IntPtr.Zero
            };

            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

            WinTrustData trustData = new()
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                PolicyCallbackData = IntPtr.Zero,
                SipClientData = IntPtr.Zero,
                UiChoice = WinTrustDataUiChoice.None,
                RevocationChecks = WinTrustDataRevocationChecks.None,
                UnionChoice = WinTrustDataChoice.File,
                FileInfo = fileInfoPointer,
                StateAction = WinTrustDataStateAction.Ignore,
                StateData = IntPtr.Zero,
                UrlReference = IntPtr.Zero,
                ProviderFlags = WinTrustProviderFlags.RevocationCheckChainExcludeRoot,
                UiContext = WinTrustDataUiContext.Execute,
                SignatureSettings = IntPtr.Zero
            };

            int status = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref trustData);
            result = MapStatus(status, signer.Publisher, signer.CertificateExpired);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            result = new(AuthenticodeTrustStatus.VerificationError, signer.HasEmbeddedSignature, false, signer.Publisher, "Windows could not complete Authenticode verification.");
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(fileInfoPointer);
            if (pathPointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pathPointer);
        }

        string afterHash;
        try
        {
            afterHash = ComputeSha256(path);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return new(AuthenticodeTrustStatus.FileChangedDuringVerification, result.IsSigned, false, result.Publisher,
                "The executable changed or became unreadable while Sentinel was verifying it.");
        }

        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(beforeHash), Convert.FromHexString(afterHash)))
        {
            return new(AuthenticodeTrustStatus.FileChangedDuringVerification, result.IsSigned, false, result.Publisher,
                "The executable changed while Sentinel was verifying it, so no trust decision was made.", afterHash);
        }

        return result with { VerifiedSha256 = afterHash };
    }

    internal static AuthenticodeVerificationResult MapStatus(int hresult, string publisher, bool signerCertificateExpired = false)
    {
        uint code = unchecked((uint)hresult);
        return code switch
        {
            0x00000000 when signerCertificateExpired => new(AuthenticodeTrustStatus.TrustedTimestamped, true, true, publisher, "Windows verified the executable and accepted its Authenticode timestamp even though the signing certificate is now expired."),
            0x00000000 => new(AuthenticodeTrustStatus.Trusted, true, true, publisher, "The executable has a valid Authenticode signature trusted by Windows."),
            0x800B0100 => new(AuthenticodeTrustStatus.Unsigned, false, false, publisher, "No Authenticode signature was found."),
            0x80096010 => new(AuthenticodeTrustStatus.ModifiedAfterSigning, true, false, publisher, "The executable content no longer matches its Authenticode signature."),
            0x800B010C => new(AuthenticodeTrustStatus.Revoked, true, false, publisher, "The signing certificate has been revoked."),
            0x800B0101 => new(AuthenticodeTrustStatus.Expired, true, false, publisher, "The signing certificate is expired and Windows did not validate an acceptable timestamp."),
            0x800B0111 => new(AuthenticodeTrustStatus.ExplicitlyDistrusted, true, false, publisher, "Windows explicitly distrusts this signer or signature."),
            0x800B0109 or 0x800B010A => new(AuthenticodeTrustStatus.UntrustedSigner, true, false, publisher, "Windows could not build a trusted certificate chain for this signer."),
            0x800B0004 => new(AuthenticodeTrustStatus.UntrustedSigner, true, false, publisher, "Windows does not trust the signer for this executable."),
            0x80092026 => new(AuthenticodeTrustStatus.UntrustedSigner, true, false, publisher, "Local security policy rejected this signature."),
            0x800B0001 or 0x800B0002 or 0x800B0003 => new(AuthenticodeTrustStatus.VerificationError, true, false, publisher, "Windows could not evaluate this signature format or trust provider."),
            _ => new(AuthenticodeTrustStatus.InvalidSignature, !string.Equals(publisher, "Unsigned", StringComparison.OrdinalIgnoreCase), false, publisher, $"Authenticode verification failed with HRESULT 0x{code:X8}.")
        };
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static SignerMetadata TryGetSigner(string path)
    {
        try
        {
            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
            using X509Certificate2 certificate2 = new(certificate);
            string simpleName = certificate2.GetNameInfo(X509NameType.SimpleName, false);
            string publisher = string.IsNullOrWhiteSpace(simpleName) ? "Unknown signer" : simpleName.Trim();
            bool expired = DateTime.UtcNow > certificate2.NotAfter.ToUniversalTime();
            return new(publisher, true, expired);
        }
        catch
        {
            // Catalog-signed files may not expose an embedded signer here. WinVerifyTrust remains authoritative
            // for embedded signatures; catalog lookup is tracked as a Windows-validation follow-up for A01.
            return new("Unsigned", false, false);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        internal uint StructSize;
        internal IntPtr FilePath;
        internal IntPtr FileHandle;
        internal IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        internal uint StructSize;
        internal IntPtr PolicyCallbackData;
        internal IntPtr SipClientData;
        internal WinTrustDataUiChoice UiChoice;
        internal WinTrustDataRevocationChecks RevocationChecks;
        internal WinTrustDataChoice UnionChoice;
        internal IntPtr FileInfo;
        internal WinTrustDataStateAction StateAction;
        internal IntPtr StateData;
        internal IntPtr UrlReference;
        internal WinTrustProviderFlags ProviderFlags;
        internal WinTrustDataUiContext UiContext;
        internal IntPtr SignatureSettings;
    }

    private enum WinTrustDataUiChoice : uint
    {
        None = 2
    }

    private enum WinTrustDataRevocationChecks : uint
    {
        None = 0
    }

    private enum WinTrustDataChoice : uint
    {
        File = 1
    }

    private enum WinTrustDataStateAction : uint
    {
        Ignore = 0
    }

    [Flags]
    private enum WinTrustProviderFlags : uint
    {
        RevocationCheckChainExcludeRoot = 0x00000080
    }

    private enum WinTrustDataUiContext : uint
    {
        Execute = 0
    }

    private sealed record SignerMetadata(string Publisher, bool HasEmbeddedSignature, bool CertificateExpired);
}

internal enum AuthenticodeTrustStatus
{
    Trusted,
    TrustedTimestamped,
    Unsigned,
    InvalidSignature,
    ModifiedAfterSigning,
    UntrustedSigner,
    Revoked,
    Expired,
    ExplicitlyDistrusted,
    VerificationError,
    FileChangedDuringVerification
}

internal sealed record AuthenticodeVerificationResult(
    AuthenticodeTrustStatus Status,
    bool IsSigned,
    bool IsTrusted,
    string Publisher,
    string Explanation,
    string VerifiedSha256 = "");
