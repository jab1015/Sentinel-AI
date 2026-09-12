using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Sentinel.App.Services.Privacy;

internal sealed class WindowsCurrentUserKeyProtector : IFileKeyProtector
{
    internal const ushort CurrentRecordVersion = 1;
    internal const ushort ModeId = 1;
    internal const ushort AlgorithmId = 1;
    private const uint CryptProtectUiForbidden = 0x1;

    public ushort ProtectionModeId => ModeId;
    public ushort WrappingAlgorithmId => AlgorithmId;

    public FileKeyProtectionRecord Wrap(ReadOnlySpan<byte> dataEncryptionKey)
    {
        if (dataEncryptionKey.Length != 32)
            throw new ArgumentException("A 256-bit file data-encryption key is required.", nameof(dataEncryptionKey));

        byte[] input = dataEncryptionKey.ToArray();
        try
        {
            byte[] protectedBytes = Protect(input);
            return new FileKeyProtectionRecord(
                CurrentRecordVersion,
                ModeId,
                AlgorithmId,
                Array.Empty<byte>(),
                protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public FileKeyUnwrapResult TryUnwrap(FileKeyProtectionRecord record)
    {
        if (record.RecordVersion != CurrentRecordVersion ||
            record.ProtectionModeId != ModeId ||
            record.WrappingAlgorithmId != AlgorithmId)
        {
            return FileKeyUnwrapResult.Fail(FileKeyUnwrapStatus.UnsupportedKeyRecord);
        }

        if (record.Parameters.Length != 0 || record.WrappedDataEncryptionKey.Length == 0)
            return FileKeyUnwrapResult.Fail(FileKeyUnwrapStatus.CorruptKeyRecord);

        try
        {
            byte[] unprotected = Unprotect(record.WrappedDataEncryptionKey);
            if (unprotected.Length != 32)
            {
                CryptographicOperations.ZeroMemory(unprotected);
                return FileKeyUnwrapResult.Fail(FileKeyUnwrapStatus.CorruptKeyRecord);
            }

            return FileKeyUnwrapResult.Success(unprotected);
        }
        catch (CryptographicException)
        {
            return FileKeyUnwrapResult.Fail(FileKeyUnwrapStatus.WrongCredentialOrAccount);
        }
        catch (Win32Exception)
        {
            return FileKeyUnwrapResult.Fail(FileKeyUnwrapStatus.ProtectionProviderUnavailable);
        }
    }

    private static byte[] Protect(byte[] plaintext)
    {
        DATA_BLOB input = CreateBlob(plaintext);
        DATA_BLOB output = default;
        try
        {
            if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return CopyBlob(output);
        }
        finally
        {
            FreeInputBlob(ref input);
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    private static byte[] Unprotect(byte[] protectedBytes)
    {
        DATA_BLOB input = CreateBlob(protectedBytes);
        DATA_BLOB output = default;
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output))
                throw new CryptographicException(Marshal.GetLastWin32Error());
            return CopyBlob(output);
        }
        finally
        {
            FreeInputBlob(ref input);
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    private static DATA_BLOB CreateBlob(byte[] data)
    {
        DATA_BLOB blob = new() { cbData = data.Length };
        blob.pbData = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, blob.pbData, data.Length);
        return blob;
    }

    private static byte[] CopyBlob(DATA_BLOB blob)
    {
        if (blob.cbData <= 0 || blob.pbData == IntPtr.Zero)
            return Array.Empty<byte>();
        byte[] value = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, value, 0, value.Length);
        return value;
    }

    private static void FreeInputBlob(ref DATA_BLOB blob)
    {
        if (blob.pbData == IntPtr.Zero) return;
        int length = Math.Max(blob.cbData, 0);
        if (length > 0)
        {
            byte[] zeros = new byte[length];
            Marshal.Copy(zeros, 0, blob.pbData, length);
        }
        Marshal.FreeHGlobal(blob.pbData);
        blob.pbData = IntPtr.Zero;
        blob.cbData = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
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
