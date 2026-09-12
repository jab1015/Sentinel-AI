using System;

namespace Sentinel.App.Services.Privacy;

internal interface IFileKeyProtector
{
    ushort ProtectionModeId { get; }
    ushort WrappingAlgorithmId { get; }

    FileKeyProtectionRecord Wrap(ReadOnlySpan<byte> dataEncryptionKey);
    FileKeyUnwrapResult TryUnwrap(FileKeyProtectionRecord record);
}

internal sealed record FileKeyProtectionRecord(
    ushort RecordVersion,
    ushort ProtectionModeId,
    ushort WrappingAlgorithmId,
    byte[] Parameters,
    byte[] WrappedDataEncryptionKey);

internal enum FileKeyUnwrapStatus
{
    Success,
    WrongCredentialOrAccount,
    UnsupportedKeyRecord,
    CorruptKeyRecord,
    ProtectionProviderUnavailable
}

internal sealed record FileKeyUnwrapResult(FileKeyUnwrapStatus Status, byte[]? DataEncryptionKey)
{
    internal static FileKeyUnwrapResult Success(byte[] key) => new(FileKeyUnwrapStatus.Success, key);
    internal static FileKeyUnwrapResult Fail(FileKeyUnwrapStatus status) => new(status, null);
}
