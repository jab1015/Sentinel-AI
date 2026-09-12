using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sentinel.App.Services;

/// <summary>
/// Non-destructive exact-target validation for future Secure Delete operations.
/// Path text never authorizes deletion. The selected file is reopened with reparse
/// traversal disabled, resolved by handle, bound to stable file identity, and checked
/// against protected-location policy. This type deliberately exposes no delete API.
/// </summary>
internal static class SecureDeleteTargetValidator
{
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const int FileIdInfoClass = 18;
    private const int FileStandardInfoClass = 1;
    private const int FileAttributeTagInfoClass = 9;
    private const uint FileNameNormalized = 0;
    private const uint VolumeNameDos = 0;
    private const int MaximumFinalPathCharacters = 32_768;

    private static readonly HashSet<string> SystemCriticalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys",
        "swapfile.sys",
        "hiberfil.sys",
        "bootmgr",
        "bootnxt"
    };

    internal static SecureDeleteTargetValidationResult Validate(string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
            return Failure(SecureDeleteTargetValidationCode.InvalidPath, "A fully qualified filesystem path is required.");

        if (IsUnsupportedNamespace(candidatePath))
            return Failure(SecureDeleteTargetValidationCode.UnsupportedNamespace, "Device and object-manager namespaces are not Secure Delete targets.");

        string requested;
        try
        {
            if (!Path.IsPathFullyQualified(candidatePath))
                return Failure(SecureDeleteTargetValidationCode.InvalidPath, "Relative paths are not Secure Delete targets.");
            requested = Path.GetFullPath(candidatePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(SecureDeleteTargetValidationCode.InvalidPath, "The selected path could not be canonicalized.");
        }

        using SafeFileHandle handle = CreateFileW(
            requested,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero);

        if (handle.IsInvalid)
            return Failure(SecureDeleteTargetValidationCode.MissingOrInaccessible, "The exact selected file could not be reopened.");

        if (!TryGetInfo(handle, FileAttributeTagInfoClass, out FILE_ATTRIBUTE_TAG_INFO attributeInfo))
            return Failure(SecureDeleteTargetValidationCode.IdentityUnverified, "File attributes could not be verified from the opened object.");

        if ((attributeInfo.FileAttributes & FileAttributeDirectory) != 0)
            return Failure(SecureDeleteTargetValidationCode.DirectoryRejected, "Secure Delete v1 accepts files only, not directories.");

        if ((attributeInfo.FileAttributes & FileAttributeReparsePoint) != 0)
            return Failure(SecureDeleteTargetValidationCode.ReparsePointRejected, "Reparse points, symbolic links, and junction-like objects are not accepted.");

        if (!TryGetInfo(handle, FileStandardInfoClass, out FILE_STANDARD_INFO standardInfo))
            return Failure(SecureDeleteTargetValidationCode.IdentityUnverified, "File link and size information could not be verified.");

        if (standardInfo.Directory)
            return Failure(SecureDeleteTargetValidationCode.DirectoryRejected, "Secure Delete v1 accepts files only, not directories.");

        if (standardInfo.NumberOfLinks != 1)
            return Failure(SecureDeleteTargetValidationCode.MultipleLinksRejected, "Files with multiple hard links require an explicit future policy and are rejected.");

        if (!TryGetFinalPath(handle, out string resolved))
            return Failure(SecureDeleteTargetValidationCode.FinalPathUnverified, "The final path of the opened object could not be verified.");

        try { resolved = Path.GetFullPath(NormalizeFinalPath(resolved)); }
        catch { return Failure(SecureDeleteTargetValidationCode.FinalPathUnverified, "The handle-resolved path was invalid."); }

        if (!resolved.Equals(requested, StringComparison.OrdinalIgnoreCase))
            return Failure(SecureDeleteTargetValidationCode.FinalPathUnverified, "The opened object resolved to a different path.");

        if (IsProtectedLocation(resolved))
            return Failure(SecureDeleteTargetValidationCode.ProtectedLocation, "Windows, Program Files, and Sentinel application locations are protected from Secure Delete.");

        if (SystemCriticalNames.Contains(Path.GetFileName(resolved)))
            return Failure(SecureDeleteTargetValidationCode.SystemCriticalFile, "System-critical paging, hibernation, or boot files are never Secure Delete targets.");

        if (!TryGetInfo(handle, FileIdInfoClass, out FILE_ID_INFO fileId) ||
            (fileId.VolumeSerialNumber == 0 && fileId.FileId.LowPart == 0 && fileId.FileId.HighPart == 0))
            return Failure(SecureDeleteTargetValidationCode.IdentityUnverified, "Stable filesystem identity could not be read.");

        long length = Math.Max(standardInfo.EndOfFile, 0);
        return SecureDeleteTargetValidationResult.Success(new SecureDeleteTargetIdentity(
            fileId.VolumeSerialNumber,
            fileId.FileId.LowPart,
            fileId.FileId.HighPart,
            resolved,
            length));
    }

    internal static SecureDeleteTargetValidationResult Revalidate(SecureDeleteTargetIdentity expected)
    {
        if (expected.IsEmpty)
            return Failure(SecureDeleteTargetValidationCode.InvalidPath, "No bound Secure Delete target identity was supplied.");

        SecureDeleteTargetValidationResult current = Validate(expected.CanonicalPath);
        if (!current.Succeeded)
            return current.Code == SecureDeleteTargetValidationCode.MissingOrInaccessible
                ? Failure(SecureDeleteTargetValidationCode.IdentityChanged, "The approved target disappeared or became inaccessible before mutation.")
                : current;

        SecureDeleteTargetIdentity actual = current.Target;
        if (actual.VolumeSerialNumber != expected.VolumeSerialNumber ||
            actual.FileIdLow != expected.FileIdLow ||
            actual.FileIdHigh != expected.FileIdHigh ||
            !actual.CanonicalPath.Equals(expected.CanonicalPath, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(SecureDeleteTargetValidationCode.IdentityChanged, "The filesystem object changed after approval.");
        }

        return current;
    }

    private static bool IsUnsupportedNamespace(string path)
    {
        string value = path.Trim();
        return value.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith(@"\\?\GLOBALROOT\", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith(@"\\?\Device\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProtectedLocation(string path)
    {
        foreach (string root in GetProtectedRoots())
        {
            if (IsSameOrUnder(path, root)) return true;
        }
        return false;
    }

    private static IEnumerable<string> GetProtectedRoots()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string applicationBase = AppContext.BaseDirectory;

        if (!string.IsNullOrWhiteSpace(windows)) yield return windows;
        if (!string.IsNullOrWhiteSpace(programFiles)) yield return programFiles;
        if (!string.IsNullOrWhiteSpace(programFilesX86)) yield return programFilesX86;
        if (!string.IsNullOrWhiteSpace(applicationBase)) yield return applicationBase;
    }

    private static bool IsSameOrUnder(string path, string root)
    {
        try
        {
            string canonicalPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return canonicalPath.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase) ||
                   canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }
    }

    private static bool TryGetFinalPath(SafeFileHandle handle, out string path)
    {
        path = string.Empty;
        uint required = GetFinalPathNameByHandleW(handle, null, 0, FileNameNormalized | VolumeNameDos);
        if (required == 0 || required > MaximumFinalPathCharacters) return false;
        StringBuilder buffer = new(checked((int)required + 1));
        uint written = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, FileNameNormalized | VolumeNameDos);
        if (written == 0 || written >= buffer.Capacity) return false;
        path = buffer.ToString();
        return !string.IsNullOrWhiteSpace(path);
    }

    private static string NormalizeFinalPath(string path)
    {
        string value = path.Trim();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + value[8..];
        if (value.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            return value[4..];
        return value;
    }

    private static bool TryGetInfo<T>(SafeFileHandle handle, int infoClass, out T value) where T : struct
    {
        value = default;
        int size = Marshal.SizeOf<T>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetFileInformationByHandleEx(handle, infoClass, buffer, (uint)size)) return false;
            value = Marshal.PtrToStructure<T>(buffer);
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static SecureDeleteTargetValidationResult Failure(SecureDeleteTargetValidationCode code, string message) =>
        SecureDeleteTargetValidationResult.Failure(code, message);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_STANDARD_INFO
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        [MarshalAs(UnmanagedType.U1)] public bool DeletePending;
        [MarshalAs(UnmanagedType.U1)] public bool Directory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ATTRIBUTE_TAG_INFO
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ID_128
    {
        public ulong LowPart;
        public ulong HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;
        public FILE_ID_128 FileId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        IntPtr fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        StringBuilder? filePath,
        uint filePathLength,
        uint flags);
}
