using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

/// <summary>
/// Wraps an independent file DEK with Windows DPAPI current-user protection.
/// This intentionally does not set CRYPTPROTECT_LOCAL_MACHINE.
/// </summary>
internal sealed class WindowsCurrentUserFileKeyProtector : IFileKeyProtector
{
    internal const ushort ModeId = 1;
    internal const ushort WrappingAlgorithmDpapiCurrentUser = 1;
    private const uint CryptProtectUiForbidden = 0x1;

    public ushort ProtectionModeId => ModeId;

    public ValueTask<WrappedFileKeyRecord> WrapAsync(
        ReadOnlyMemory<byte> dataEncryptionKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dataEncryptionKey.Length != 32)
            throw new ArgumentException("A 256-bit file data-encryption key is required.", nameof(dataEncryptionKey));

        byte[] input = dataEncryptionKey.ToArray();
        try
        {
            byte[] protectedBlob = ProtectCurrentUser(input);
            return ValueTask.FromResult(new WrappedFileKeyRecord(
                RecordVersion: 1,
                ProtectionModeId: ModeId,
                WrappingAlgorithmId: WrappingAlgorithmDpapiCurrentUser,
                Parameters: Array.Empty<byte>(),
                WrappedDek: protectedBlob));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public ValueTask<byte[]?> TryUnwrapAsync(
        WrappedFileKeyRecord record,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record is null ||
            record.RecordVersion != 1 ||
            record.ProtectionModeId != ModeId ||
            record.WrappingAlgorithmId != WrappingAlgorithmDpapiCurrentUser ||
            record.Parameters.Length != 0 ||
            record.WrappedDek.Length == 0)
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        try
        {
            byte[] plaintext = UnprotectCurrentUser(record.WrappedDek);
            if (plaintext.Length != 32)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                return ValueTask.FromResult<byte[]?>(null);
            }
            return ValueTask.FromResult<byte[]?>(plaintext);
        }
        catch (CryptographicException)
        {
            return ValueTask.FromResult<byte[]?>(null);
        }
    }

    private static byte[] ProtectCurrentUser(byte[] plaintext)
    {
        DATA_BLOB input = AllocateBlob(plaintext);
        DATA_BLOB output = default;
        try
        {
            if (!CryptProtectData(
                ref input,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                CryptProtectUiForbidden,
                out output))
            {
                int error = Marshal.GetLastWin32Error();
                throw new CryptographicException($"Windows current-user key protection failed ({error}: {new Win32Exception(error).Message}).");
            }

            if (output.cbData <= 0 || output.pbData == IntPtr.Zero)
                throw new CryptographicException("Windows current-user key protection returned an empty protected blob.");

            byte[] protectedBlob = new byte[output.cbData];
            Marshal.Copy(output.pbData, protectedBlob, 0, protectedBlob.Length);
            return protectedBlob;
        }
        finally
        {
            ZeroAndFreeHGlobal(input);
            FreeLocalBlob(output, containsPlaintext: false);
        }
    }

    private static byte[] UnprotectCurrentUser(byte[] protectedBlob)
    {
        DATA_BLOB input = AllocateBlob(protectedBlob);
        DATA_BLOB output = default;
        try
        {
            if (!CryptUnprotectData(
                ref input,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                CryptProtectUiForbidden,
                out output))
            {
                int error = Marshal.GetLastWin32Error();
                throw new CryptographicException($"Windows current-user key unprotection failed ({error}: {new Win32Exception(error).Message}).");
            }

            if (output.cbData <= 0 || output.pbData == IntPtr.Zero)
                throw new CryptographicException("Windows current-user key unprotection returned an empty key.");

            byte[] plaintext = new byte[output.cbData];
            Marshal.Copy(output.pbData, plaintext, 0, plaintext.Length);
            return plaintext;
        }
        finally
        {
            ZeroAndFreeHGlobal(input);
            FreeLocalBlob(output, containsPlaintext: true);
        }
    }

    private static DATA_BLOB AllocateBlob(byte[] data)
    {
        if (data.Length == 0) return default;
        IntPtr pointer = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, pointer, data.Length);
            return new DATA_BLOB { cbData = data.Length, pbData = pointer };
        }
        catch
        {
            Marshal.FreeHGlobal(pointer);
            throw;
        }
    }

    private static void ZeroAndFreeHGlobal(DATA_BLOB blob)
    {
        if (blob.pbData == IntPtr.Zero) return;
        try
        {
            if (blob.cbData > 0)
                Marshal.Copy(new byte[blob.cbData], 0, blob.pbData, blob.cbData);
        }
        finally
        {
            Marshal.FreeHGlobal(blob.pbData);
        }
    }

    private static void FreeLocalBlob(DATA_BLOB blob, bool containsPlaintext)
    {
        if (blob.pbData == IntPtr.Zero) return;
        try
        {
            if (containsPlaintext && blob.cbData > 0)
                Marshal.Copy(new byte[blob.cbData], 0, blob.pbData, blob.cbData);
        }
        finally
        {
            _ = LocalFree(blob.pbData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        internal int cbData;
        internal IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        IntPtr szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
