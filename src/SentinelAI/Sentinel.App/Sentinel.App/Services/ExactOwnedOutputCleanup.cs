using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sentinel.App.Services;

/// <summary>
/// Captures stable Windows file identity for an output Sentinel just created and later
/// removes only that same object. A pathname replacement is never accepted as the owned
/// artifact merely because it now occupies the same name.
/// </summary>
internal static class ExactOwnedOutputCleanup
{
    private const uint DeleteAccess = 0x00010000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const int FileIdInfoClass = 18;
    private const int FileDispositionInfoClass = 4;
    private const uint FileNameNormalized = 0;
    private const uint VolumeNameDos = 0;

    internal static bool TryCapture(FileStream stream, string expectedPath, out OwnedFileIdentity identity)
    {
        identity = default;
        ArgumentNullException.ThrowIfNull(stream);
        if (string.IsNullOrWhiteSpace(expectedPath) || stream.SafeFileHandle.IsInvalid)
            return false;

        if (!TryGetIdentity(stream.SafeFileHandle, out FILE_ID_INFO info) ||
            !TryGetFinalPath(stream.SafeFileHandle, out string finalPath))
            return false;

        string expected;
        try { expected = Path.GetFullPath(expectedPath); }
        catch { return false; }

        string resolved = NormalizeFinalPath(finalPath);
        try { resolved = Path.GetFullPath(resolved); }
        catch { return false; }

        if (!resolved.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return false;

        identity = new OwnedFileIdentity(info.VolumeSerialNumber, info.FileId.LowPart, info.FileId.HighPart, resolved);
        return true;
    }

    internal static bool TryDeleteSameObject(string path, OwnedFileIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(path) || identity.IsEmpty)
            return false;

        string expected;
        try { expected = Path.GetFullPath(path); }
        catch { return false; }
        if (!expected.Equals(identity.CanonicalPath, StringComparison.OrdinalIgnoreCase))
            return false;

        using SafeFileHandle handle = CreateFileW(
            expected,
            DeleteAccess | FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
            return !File.Exists(expected);

        if (!TryGetIdentity(handle, out FILE_ID_INFO current) ||
            current.VolumeSerialNumber != identity.VolumeSerialNumber ||
            current.FileId.LowPart != identity.FileIdLow ||
            current.FileId.HighPart != identity.FileIdHigh)
            return false;

        if (!TryGetFinalPath(handle, out string finalPath))
            return false;
        string resolved = NormalizeFinalPath(finalPath);
        try { resolved = Path.GetFullPath(resolved); }
        catch { return false; }
        if (!resolved.Equals(identity.CanonicalPath, StringComparison.OrdinalIgnoreCase))
            return false;

        FILE_DISPOSITION_INFO disposition = new() { DeleteFile = true };
        if (!SetFileInformationByHandle(
                handle,
                FileDispositionInfoClass,
                ref disposition,
                (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO>()))
            return false;

        return true;
    }

    private static bool TryGetIdentity(SafeFileHandle handle, out FILE_ID_INFO info)
    {
        info = default;
        int size = Marshal.SizeOf<FILE_ID_INFO>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetFileInformationByHandleEx(handle, FileIdInfoClass, buffer, (uint)size))
                return false;
            info = Marshal.PtrToStructure<FILE_ID_INFO>(buffer);
            return info.VolumeSerialNumber != 0 || info.FileId.LowPart != 0 || info.FileId.HighPart != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryGetFinalPath(SafeFileHandle handle, out string path)
    {
        path = string.Empty;
        uint required = GetFinalPathNameByHandleW(handle, null, 0, FileNameNormalized | VolumeNameDos);
        if (required == 0 || required > 32_768)
            return false;
        StringBuilder buffer = new(checked((int)required + 1));
        uint written = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, FileNameNormalized | VolumeNameDos);
        if (written == 0 || written >= buffer.Capacity)
            return false;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_DISPOSITION_INFO
    {
        [MarshalAs(UnmanagedType.U1)] public bool DeleteFile;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        ref FILE_DISPOSITION_INFO fileInformation,
        uint bufferSize);
}

internal readonly record struct OwnedFileIdentity(
    ulong VolumeSerialNumber,
    ulong FileIdLow,
    ulong FileIdHigh,
    string CanonicalPath)
{
    internal bool IsEmpty => string.IsNullOrWhiteSpace(CanonicalPath);
}
