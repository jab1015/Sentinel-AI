using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Sentinel.App.Services;

internal enum SecureDeleteMutationLeaseCode
{
    Acquired = 0,
    MutationGateRejected,
    OpenDeniedOrMissing,
    FinalPathUnverified,
    AttributesUnverified,
    ReparsePointRejected,
    DirectoryRejected,
    MultipleLinksRejected,
    IdentityUnverified,
    IdentityChanged
}

/// <summary>
/// Holds the exact filesystem object open after independent mutation-time verification.
/// The live handle intentionally denies write/delete sharing so the approved object cannot
/// be renamed, replaced, or opened for new writes while a future narrow mutation executes.
/// This type exposes no delete, overwrite, truncate, or raw-handle operation.
/// </summary>
internal sealed class SecureDeleteMutationLease : IDisposable
{
    private SafeFileHandle? _handle;

    internal SecureDeleteMutationLease(
        SafeFileHandle handle,
        Guid authorizationId,
        SecureDeleteTargetIdentity target,
        StorageCapabilitySnapshot storage)
    {
        _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        AuthorizationId = authorizationId;
        Target = target;
        Storage = storage;
    }

    internal Guid AuthorizationId { get; }
    internal SecureDeleteTargetIdentity Target { get; }
    internal StorageCapabilitySnapshot Storage { get; }
    internal bool IsActive => _handle is { IsInvalid: false, IsClosed: false };

    public void Dispose()
    {
        SafeFileHandle? handle = Interlocked.Exchange(ref _handle, null);
        handle?.Dispose();
    }
}

internal sealed record SecureDeleteMutationLeaseResult(
    bool Succeeded,
    SecureDeleteMutationLeaseCode Code,
    string Message,
    SecureDeleteMutationLease? Lease,
    SecureDeleteCoordinatorCode CoordinatorCode)
{
    internal static SecureDeleteMutationLeaseResult Success(SecureDeleteMutationLease lease) =>
        new(true, SecureDeleteMutationLeaseCode.Acquired,
            "Exact Secure Delete target is retained by a non-destructive mutation lease.",
            lease, SecureDeleteCoordinatorCode.MutationGateReady);

    internal static SecureDeleteMutationLeaseResult Failure(
        SecureDeleteMutationLeaseCode code,
        string message,
        SecureDeleteCoordinatorCode coordinatorCode = SecureDeleteCoordinatorCode.MutationGateReady) =>
        new(false, code, message, null, coordinatorCode);
}

/// <summary>
/// Acquires a non-destructive, retained exact-object lease for a previously prepared
/// Secure Delete authorization. Path text is used only to reopen the authorized object;
/// stable handle-derived identity must match before the lease is returned.
/// </summary>
internal sealed class SecureDeleteMutationLeaseManager
{
    private const uint DeleteAccess = 0x00010000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const int FileStandardInfoClass = 1;
    private const int FileAttributeTagInfoClass = 9;
    private const int FileIdInfoClass = 18;
    private const uint FileNameNormalized = 0;
    private const uint VolumeNameDos = 0;
    private const uint MaximumFinalPathCharacters = 32_768;

    private readonly SecureDeleteCoordinator _coordinator;

    internal SecureDeleteMutationLeaseManager(SecureDeleteCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    internal SecureDeleteMutationLeaseResult Acquire(SecureDeleteAuthorization? authorization)
    {
        SecureDeleteMutationGateResult gate = _coordinator.RevalidateForMutation(authorization);
        if (!gate.Succeeded || authorization is null)
        {
            return SecureDeleteMutationLeaseResult.Failure(
                SecureDeleteMutationLeaseCode.MutationGateRejected,
                "Secure Delete mutation preflight did not authorize exact-handle acquisition: " + gate.Message,
                gate.Code);
        }

        SafeFileHandle handle = CreateFileW(
            gate.Target.CanonicalPath,
            DeleteAccess | FileReadAttributes,
            FileShareRead,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return SecureDeleteMutationLeaseResult.Failure(
                SecureDeleteMutationLeaseCode.OpenDeniedOrMissing,
                "The exact target could not be opened with exclusive mutation-relevant sharing semantics.");
        }

        try
        {
            if (!TryGetFinalPath(handle, out string finalPath))
                return FailAndClose(handle, SecureDeleteMutationLeaseCode.FinalPathUnverified,
                    "The live mutation lease handle did not yield a verified final path.");

            try { finalPath = Path.GetFullPath(NormalizeFinalPath(finalPath)); }
            catch
            {
                return FailAndClose(handle, SecureDeleteMutationLeaseCode.FinalPathUnverified,
                    "The live mutation lease handle yielded an invalid final path.");
            }

            if (!finalPath.Equals(gate.Target.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                return FailAndClose(handle, SecureDeleteMutationLeaseCode.IdentityChanged,
                    "The opened object resolved to a different canonical path.");

            if (!TryGetInfo(handle, FileAttributeTagInfoClass, out FILE_ATTRIBUTE_TAG_INFO attributes))
                return FailAndClose(handle, SecureDeleteMutationLeaseCode.AttributesUnverified,
                    "The live target attributes could not be verified.");

            if ((attributes.FileAttributes & FileAttributeReparsePoint) != 0)
                return FailAndClose(handle, SecureDeleteMutationLeaseCode.ReparsePointRejected,
                    "Reparse targets cannot receive a Secure Delete mutation lease.");

            if ((attributes.FileAttributes & FileAttributeDirectory) != 0)
                return FailAndClose(handle, SecureDeleteMutationLeaseCode.DirectoryRejected,
                    "Directories cannot receive a Secure Delete mutation lease.");

            if (!TryGetInfo(handle, FileStandardInfoClass, out FILE_STANDARD_INFO standard))
                return FailAndClose(handle, SecureDeleteMutationLeaseCode.IdentityUnverified,
                    "The live target link and size state could not be verified.");

            if (standard.Directory)
                return FailAndClose(handle, SecureDeleteMutationLeaseCode.DirectoryRejected,
                    "Directories cannot receive a Secure Delete mutation lease.");

            if (standard.NumberOfLinks != 1)
                return FailAndClose(handle, SecureDeleteMutationLeaseCode.MultipleLinksRejected,
                    "Multiply-linked files cannot receive a Secure Delete mutation lease.");

            if (!TryGetInfo(handle, FileIdInfoClass, out FILE_ID_INFO fileId))
                return FailAndClose(handle, SecureDeleteMutationLeaseCode.IdentityUnverified,
                    "Stable identity could not be read from the retained target handle.");

            if (fileId.VolumeSerialNumber != gate.Target.VolumeSerialNumber ||
                fileId.FileId.LowPart != gate.Target.FileIdLow ||
                fileId.FileId.HighPart != gate.Target.FileIdHigh)
            {
                return FailAndClose(handle, SecureDeleteMutationLeaseCode.IdentityChanged,
                    "The retained filesystem object did not match the authorized stable identity.");
            }

            SecureDeleteMutationLease lease = new(handle, authorization.AuthorizationId, gate.Target, gate.Storage!);
            handle = null!;
            return SecureDeleteMutationLeaseResult.Success(lease);
        }
        finally
        {
            handle?.Dispose();
        }
    }

    private static SecureDeleteMutationLeaseResult FailAndClose(
        SafeFileHandle handle,
        SecureDeleteMutationLeaseCode code,
        string message)
    {
        handle.Dispose();
        return SecureDeleteMutationLeaseResult.Failure(code, message);
    }

    private static bool TryGetFinalPath(SafeFileHandle handle, out string path)
    {
        path = string.Empty;
        uint required = GetFinalPathNameByHandleW(handle, null, 0, FileNameNormalized | VolumeNameDos);
        if (required == 0 || required > MaximumFinalPathCharacters) return false;
        StringBuilder buffer = new(checked((int)required + 1));
        uint written = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, FileNameNormalized | VolumeNameDos);
        if (written == 0 || written >= (uint)buffer.Capacity) return false;
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
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

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
