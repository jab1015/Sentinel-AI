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
    private static readonly Guid DriverActionVerify =
        new("F750E6C3-38EE-11D1-85E5-00C04FC295EE");

    internal static AuthenticodeVerificationResult Verify(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new(AuthenticodeTrustStatus.VerificationError, false, false, "Unknown", "The executable could not be opened for signature verification.");

        string beforeHash;
        try { beforeHash = ComputeSha256(path); }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return new(AuthenticodeTrustStatus.VerificationError, false, false, "Unknown", "The executable could not be read completely for signature verification.");
        }

        SignerMetadata embeddedSigner = TryGetSigner(path);
        AuthenticodeVerificationResult result = VerifyEmbedded(path, embeddedSigner);

        if (result.Status == AuthenticodeTrustStatus.Unsigned)
        {
            AuthenticodeVerificationResult catalog = VerifyCatalog(path);
            if (catalog.Status != AuthenticodeTrustStatus.Unsigned)
                result = catalog;
        }

        string afterHash;
        try { afterHash = ComputeSha256(path); }
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

    private static AuthenticodeVerificationResult VerifyEmbedded(string path, SignerMetadata signer)
    {
        IntPtr fileInfoPointer = IntPtr.Zero;
        IntPtr pathPointer = IntPtr.Zero;
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
            WinTrustData trustData = CreateTrustData(WinTrustDataChoice.File, fileInfoPointer);
            int status = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref trustData);
            return MapStatus(status, signer.Publisher, signer.CertificateExpired);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new(AuthenticodeTrustStatus.VerificationError, signer.HasEmbeddedSignature, false, signer.Publisher, "Windows could not complete embedded Authenticode verification.");
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfoPointer);
            if (pathPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    private static AuthenticodeVerificationResult VerifyCatalog(string path)
    {
        IntPtr catAdmin = IntPtr.Zero;
        IntPtr catInfo = IntPtr.Zero;
        IntPtr catalogInfoPointer = IntPtr.Zero;
        IntPtr catalogPathPointer = IntPtr.Zero;
        IntPtr memberTagPointer = IntPtr.Zero;
        IntPtr memberPathPointer = IntPtr.Zero;
        IntPtr hashPointer = IntPtr.Zero;

        try
        {
            Guid subsystem = DriverActionVerify;
            if (!CryptCATAdminAcquireContext2(out catAdmin, ref subsystem, "SHA256", IntPtr.Zero, 0) || catAdmin == IntPtr.Zero)
                return new(AuthenticodeTrustStatus.Unsigned, false, false, "Unsigned", "No embedded Authenticode signature was found and Windows catalog lookup was unavailable.");

            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            IntPtr fileHandle = stream.SafeFileHandle.DangerousGetHandle();
            uint hashSize = 0;
            if (!CryptCATAdminCalcHashFromFileHandle2(catAdmin, fileHandle, ref hashSize, null, 0) || hashSize == 0 || hashSize > 1024)
                return new(AuthenticodeTrustStatus.Unsigned, false, false, "Unsigned", "No embedded Authenticode signature or usable catalog hash was found.");

            byte[] catalogHash = new byte[hashSize];
            if (!CryptCATAdminCalcHashFromFileHandle2(catAdmin, fileHandle, ref hashSize, catalogHash, 0))
                return new(AuthenticodeTrustStatus.Unsigned, false, false, "Unsigned", "Windows could not calculate the catalog membership hash.");

            catInfo = CryptCATAdminEnumCatalogFromHash(catAdmin, catalogHash, hashSize, 0, IntPtr.Zero);
            if (catInfo == IntPtr.Zero)
                return new(AuthenticodeTrustStatus.Unsigned, false, false, "Unsigned", "No embedded signature or Windows catalog membership was found.");

            CatalogInfo catalog = new() { StructSize = (uint)Marshal.SizeOf<CatalogInfo>() };
            if (!CryptCATCatalogInfoFromContext(catInfo, ref catalog, 0) || string.IsNullOrWhiteSpace(catalog.CatalogFilePath))
                return new(AuthenticodeTrustStatus.VerificationError, true, false, "Catalog signer unavailable", "Windows found catalog membership but could not resolve the catalog file.");

            string memberTag = Convert.ToHexString(catalogHash);
            catalogPathPointer = Marshal.StringToCoTaskMemUni(catalog.CatalogFilePath);
            memberTagPointer = Marshal.StringToCoTaskMemUni(memberTag);
            memberPathPointer = Marshal.StringToCoTaskMemUni(path);
            hashPointer = Marshal.AllocCoTaskMem(catalogHash.Length);
            Marshal.Copy(catalogHash, 0, hashPointer, catalogHash.Length);

            WinTrustCatalogInfo trustCatalog = new()
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustCatalogInfo>(),
                CatalogVersion = 0,
                CatalogFilePath = catalogPathPointer,
                MemberTag = memberTagPointer,
                MemberFilePath = memberPathPointer,
                MemberFile = fileHandle,
                CalculatedFileHash = hashPointer,
                CalculatedFileHashSize = (uint)catalogHash.Length,
                CatalogContext = catInfo,
                CatAdmin = catAdmin
            };

            catalogInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustCatalogInfo>());
            Marshal.StructureToPtr(trustCatalog, catalogInfoPointer, false);
            WinTrustData trustData = CreateTrustData(WinTrustDataChoice.Catalog, catalogInfoPointer);
            int status = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref trustData);

            SignerMetadata catalogSigner = TryGetSigner(catalog.CatalogFilePath);
            string publisher = catalogSigner.HasEmbeddedSignature ? catalogSigner.Publisher : "Trusted Windows catalog signer";
            AuthenticodeVerificationResult mapped = MapStatus(status, publisher, catalogSigner.CertificateExpired);
            return mapped.IsTrusted
                ? mapped with { Explanation = "Windows verified this file through a trusted Authenticode catalog and validated the file's catalog membership hash." }
                : mapped;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ExternalException)
        {
            return new(AuthenticodeTrustStatus.VerificationError, true, false, "Catalog signer unavailable", "Windows could not complete catalog-signature verification.");
        }
        finally
        {
            if (catalogInfoPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(catalogInfoPointer);
            if (catalogPathPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(catalogPathPointer);
            if (memberTagPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(memberTagPointer);
            if (memberPathPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(memberPathPointer);
            if (hashPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(hashPointer);
            if (catInfo != IntPtr.Zero && catAdmin != IntPtr.Zero) CryptCATAdminReleaseCatalogContext(catAdmin, catInfo, 0);
            if (catAdmin != IntPtr.Zero) CryptCATAdminReleaseContext(catAdmin, 0);
        }
    }

    private static WinTrustData CreateTrustData(WinTrustDataChoice choice, IntPtr unionPointer) => new()
    {
        StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
        PolicyCallbackData = IntPtr.Zero,
        SipClientData = IntPtr.Zero,
        UiChoice = WinTrustDataUiChoice.None,
        RevocationChecks = WinTrustDataRevocationChecks.None,
        UnionChoice = choice,
        FileInfo = unionPointer,
        StateAction = WinTrustDataStateAction.Ignore,
        StateData = IntPtr.Zero,
        UrlReference = IntPtr.Zero,
        ProviderFlags = WinTrustProviderFlags.RevocationCheckChainExcludeRoot,
        UiContext = WinTrustDataUiContext.Execute,
        SignatureSettings = IntPtr.Zero
    };

    internal static AuthenticodeVerificationResult MapStatus(int hresult, string publisher, bool signerCertificateExpired = false)
    {
        uint code = unchecked((uint)hresult);
        return code switch
        {
            0x00000000 when signerCertificateExpired => new(AuthenticodeTrustStatus.TrustedTimestamped, true, true, publisher, "Windows verified the signature and accepted its trusted timestamp even though the signing certificate is now expired."),
            0x00000000 => new(AuthenticodeTrustStatus.Trusted, true, true, publisher, "Windows verified the Authenticode signature and trust chain."),
            0x800B0100 => new(AuthenticodeTrustStatus.Unsigned, false, false, publisher, "No Authenticode signature was found."),
            0x80096010 => new(AuthenticodeTrustStatus.ModifiedAfterSigning, true, false, publisher, "The file content no longer matches its Authenticode signature or catalog membership hash."),
            0x800B010C => new(AuthenticodeTrustStatus.Revoked, true, false, publisher, "The signing certificate has been revoked."),
            0x800B0101 => new(AuthenticodeTrustStatus.Expired, true, false, publisher, "The signing certificate is expired and Windows did not validate an acceptable timestamp."),
            0x800B0111 => new(AuthenticodeTrustStatus.ExplicitlyDistrusted, true, false, publisher, "Windows explicitly distrusts this signer or signature."),
            0x800B0109 or 0x800B010A => new(AuthenticodeTrustStatus.UntrustedSigner, true, false, publisher, "Windows could not build a trusted certificate chain for this signer."),
            0x800B0004 => new(AuthenticodeTrustStatus.UntrustedSigner, true, false, publisher, "Windows does not trust the signer for this file."),
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
            return new("Unsigned", false, false);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, ref WinTrustData trustData);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminAcquireContext2(out IntPtr catAdmin, ref Guid subsystem, string hashAlgorithm, IntPtr strongHashPolicy, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr catAdmin, IntPtr fileHandle, ref uint hashSize, [Out] byte[]? hash, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr catAdmin, byte[] hash, uint hashSize, uint flags, IntPtr previousCatalogInfo);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATCatalogInfoFromContext(IntPtr catalogInfo, ref CatalogInfo info, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr catAdmin, IntPtr catalogInfo, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseContext(IntPtr catAdmin, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        internal uint StructSize;
        internal IntPtr FilePath;
        internal IntPtr FileHandle;
        internal IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustCatalogInfo
    {
        internal uint StructSize;
        internal uint CatalogVersion;
        internal IntPtr CatalogFilePath;
        internal IntPtr MemberTag;
        internal IntPtr MemberFilePath;
        internal IntPtr MemberFile;
        internal IntPtr CalculatedFileHash;
        internal uint CalculatedFileHashSize;
        internal IntPtr CatalogContext;
        internal IntPtr CatAdmin;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CatalogInfo
    {
        internal uint StructSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string CatalogFilePath;
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

    private enum WinTrustDataUiChoice : uint { None = 2 }
    private enum WinTrustDataRevocationChecks : uint { None = 0 }
    private enum WinTrustDataChoice : uint { File = 1, Catalog = 2 }
    private enum WinTrustDataStateAction : uint { Ignore = 0 }
    [Flags] private enum WinTrustProviderFlags : uint { RevocationCheckChainExcludeRoot = 0x00000080 }
    private enum WinTrustDataUiContext : uint { Execute = 0 }

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
