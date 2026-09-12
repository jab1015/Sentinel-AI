using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal interface IFileKeyProtector
{
    ushort ProtectionModeId { get; }

    ValueTask<WrappedFileKeyRecord> WrapAsync(
        ReadOnlyMemory<byte> dataEncryptionKey,
        CancellationToken cancellationToken);

    ValueTask<byte[]?> TryUnwrapAsync(
        WrappedFileKeyRecord record,
        CancellationToken cancellationToken);
}

internal sealed record WrappedFileKeyRecord(
    ushort RecordVersion,
    ushort ProtectionModeId,
    ushort WrappingAlgorithmId,
    byte[] Parameters,
    byte[] WrappedDek);

internal sealed record FileEncryptionResult(
    bool Succeeded,
    bool Verified,
    string Code,
    string Message,
    string SourcePath,
    string OutputPath,
    long PlaintextBytes,
    bool InvalidOutputRemains)
{
    internal static FileEncryptionResult Failure(
        string code,
        string message,
        string sourcePath,
        string outputPath,
        long plaintextBytes = 0,
        bool invalidOutputRemains = false) =>
        new(false, false, code, message, sourcePath, outputPath, plaintextBytes, invalidOutputRemains);
}

internal sealed record FileContainerVerificationResult(
    bool Succeeded,
    string Code,
    string Message,
    long PlaintextBytes,
    long ChunkCount,
    int ChunkSize)
{
    internal static FileContainerVerificationResult Failure(string code, string message) =>
        new(false, code, message, 0, 0, 0);
}

internal sealed record FileDecryptionResult(
    bool Succeeded,
    string Code,
    string Message,
    string ContainerPath,
    string OutputPath,
    long PlaintextBytes,
    bool InvalidOutputRemains)
{
    internal static FileDecryptionResult Failure(
        string code,
        string message,
        string containerPath,
        string outputPath,
        bool invalidOutputRemains = false) =>
        new(false, code, message, containerPath, outputPath, 0, invalidOutputRemains);
}
