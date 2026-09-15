using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sentinel.App.Services;

/// <summary>
/// Reopens a File Explorer selection as a Windows filesystem object and returns the
/// handle-resolved canonical path. Explorer-provided text is never treated as object
/// identity by itself.
/// </summary>
internal static class ExplorerSelectionObjectValidator
{
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileNameNormalized = 0x0;
    private const uint VolumeNameDos = 0x0;
    private const int MaximumFinalPathCharacters = 32_768;

    internal static bool TryReopenAndResolve(string candidatePath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidatePath)) return false;

        string fullPath;
        try
        {
            if (!Path.IsPathFullyQualified(candidatePath)) return false;
            fullPath = Path.GetFullPath(candidatePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        using SafeFileHandle handle = CreateFileW(
            fullPath,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid) return false;

        uint required = GetFinalPathNameByHandleW(handle, null, 0, FileNameNormalized | VolumeNameDos);
        if (required == 0 || required > MaximumFinalPathCharacters) return false;

        StringBuilder buffer = new(checked((int)required + 1));
        uint written = GetFinalPathNameByHandleW(
            handle,
            buffer,
            (uint)buffer.Capacity,
            FileNameNormalized | VolumeNameDos);
        if (written == 0 || written >= buffer.Capacity) return false;

        string normalized = NormalizeFinalPath(buffer.ToString());
        try
        {
            if (!Path.IsPathFullyQualified(normalized)) return false;
            resolvedPath = Path.GetFullPath(normalized);
            return resolvedPath.Length > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            resolvedPath = string.Empty;
            return false;
        }
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
}
