/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sentinel.App.Services;

/// <summary>
/// Deletes only the exact file object opened through a Windows handle. The final
/// handle-resolved path must remain inside the independently handle-resolved temp
/// root, the target must not be a reparse point/directory/system/read-only object,
/// and its age/size are read from that same handle immediately before deletion.
/// </summary>
internal static class HandleBasedTemporaryFileDeletion
{
    private const uint DeleteAccess = 0x00010000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileAttributeReadOnly = 0x00000001;
    private const uint FileAttributeSystem = 0x00000004;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const int FileBasicInfoClass = 0;
    private const int FileStandardInfoClass = 1;
    private const int FileAttributeTagInfoClass = 9;
    private const int FileDispositionInfoClass = 4;
    private const uint FileNameNormalized = 0x0;
    private const uint VolumeNameDos = 0x0;

    internal static bool TryGetCanonicalDirectoryPath(string directoryPath, out string canonicalPath)
    {
        canonicalPath = string.Empty;
        if (string.IsNullOrWhiteSpace(directoryPath)) return false;
        string full;
        try { full = Path.GetFullPath(directoryPath); }
        catch { return false; }
        if (!Directory.Exists(full)) return false;

        using SafeFileHandle handle = CreateFileW(
            full,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid) return false;
        if (!TryGetFinalPath(handle, out canonicalPath)) return false;
        canonicalPath = NormalizeFinalPath(canonicalPath);
        return Path.IsPathFullyQualified(canonicalPath);
    }

    internal static HandleDeletionResult TryDeleteStaleFile(
        string candidatePath,
        string canonicalRoot,
        DateTime cutoffUtc)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(canonicalRoot))
            return HandleDeletionResult.Skip("InvalidPath");

        string fullCandidate;
        try { fullCandidate = Path.GetFullPath(candidatePath); }
        catch { return HandleDeletionResult.Skip("InvalidPath"); }

        using SafeFileHandle handle = CreateFileW(
            fullCandidate,
            DeleteAccess | FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
            return HandleDeletionResult.Skip("OpenDeniedOrMissing");

        if (!TryGetFinalPath(handle, out string finalPath))
            return HandleDeletionResult.Skip("FinalPathUnverified");
        finalPath = NormalizeFinalPath(finalPath);
        if (!IsStrictlyUnderRoot(finalPath, canonicalRoot))
            return HandleDeletionResult.Skip("ResolvedOutsideTempRoot");

        if (!TryGetInfo(handle, FileAttributeTagInfoClass, out FILE_ATTRIBUTE_TAG_INFO attributes))
            return HandleDeletionResult.Skip("AttributesUnverified");
        if ((attributes.FileAttributes & (FileAttributeDirectory | FileAttributeReparsePoint | FileAttributeSystem | FileAttributeReadOnly)) != 0)
            return HandleDeletionResult.Skip("ProtectedOrReparseObject");

        if (!TryGetInfo(handle, FileBasicInfoClass, out FILE_BASIC_INFO basic))
            return HandleDeletionResult.Skip("TimestampUnverified");
        DateTime lastWriteUtc;
        try { lastWriteUtc = DateTime.FromFileTimeUtc(basic.LastWriteTime); }
        catch { return HandleDeletionResult.Skip("TimestampInvalid"); }
        if (lastWriteUtc > cutoffUtc)
            return HandleDeletionResult.Skip("TooRecent");

        if (!TryGetInfo(handle, FileStandardInfoClass, out FILE_STANDARD_INFO standard))
            return HandleDeletionResult.Skip("SizeUnverified");
        if (standard.Directory || standard.NumberOfLinks != 1)
            return HandleDeletionResult.Skip(standard.Directory ? "DirectoryRejected" : "MultipleLinksRejected");

        FILE_DISPOSITION_INFO disposition = new() { DeleteFile = true };
        if (!SetFileInformationByHandle(
                handle,
                FileDispositionInfoClass,
                ref disposition,
                (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO>()))
            return HandleDeletionResult.Skip("DeleteByHandleRejected");

        return HandleDeletionResult.DeletedExactObject(Math.Max(standard.EndOfFile, 0));
    }

    internal static bool IsStrictlyUnderRoot(string resolvedPath, string canonicalRoot)
    {
        try
        {
            string path = Path.GetFullPath(NormalizeFinalPath(resolvedPath));
            string root = Path.GetFullPath(NormalizeFinalPath(canonicalRoot))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                   !path.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool TryGetFinalPath(SafeFileHandle handle, out string path)
    {
        path = string.Empty;
        uint required = GetFinalPathNameByHandleW(handle, null, 0, FileNameNormalized | VolumeNameDos);
        if (required == 0 || required > 32_768) return false;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_BASIC_INFO
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_STANDARD_INFO
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        [MarshalAs(UnmanagedType.Bool)] public bool DeletePending;
        [MarshalAs(UnmanagedType.Bool)] public bool Directory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ATTRIBUTE_TAG_INFO
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_DISPOSITION_INFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool DeleteFile;
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        StringBuilder? filePath,
        uint filePathLength,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        IntPtr fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        ref FILE_DISPOSITION_INFO fileInformation,
        uint bufferSize);
}

internal sealed record HandleDeletionResult(bool Deleted, long FileBytes, string Reason)
{
    internal static HandleDeletionResult DeletedExactObject(long bytes) => new(true, bytes, "DeletedExactHandleObject");
    internal static HandleDeletionResult Skip(string reason) => new(false, 0, reason);
}
