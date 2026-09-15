using Sentinel.App.Services;
using System.Buffers.Binary;
using System.Security.Cryptography;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void RunAcceptance(string name, Action action)
{
    Console.WriteLine($"--- {name}: START ---");
    action();
    Console.WriteLine($"--- {name}: COMPLETE ---");
}

static byte[] PatternBytes(int length)
{
    byte[] data = new byte[length];
    for (int i = 0; i < data.Length; i++) data[i] = (byte)((i * 31 + 17) & 0xff);
    return data;
}

static string MutatedCopy(string source, string destination, Action<byte[]> mutation)
{
    byte[] bytes = File.ReadAllBytes(source);
    mutation(bytes);
    File.WriteAllBytes(destination, bytes);
    return destination;
}

static int HeaderLength(byte[] container) => BinaryPrimitives.ReadInt32LittleEndian(container.AsSpan(10, 4));
static int DataOffset(byte[] container) => checked(HeaderLength(container) + 16);

Console.WriteLine("=== Sentinel File Encryption Acceptance ===");

RunAcceptance(nameof(ExactOwnedOutputCleanupAcceptance), ExactOwnedOutputCleanupAcceptance.Verify);
RunAcceptance(nameof(EncryptionReplacementAcceptance), EncryptionReplacementAcceptance.Verify);
RunAcceptance(nameof(PasswordProtectionAcceptance), PasswordProtectionAcceptance.Verify);
RunAcceptance(nameof(RecoveryKeyAcceptance), RecoveryKeyAcceptance.Verify);
RunAcceptance(nameof(WindowsCurrentUserProtectionAcceptance), WindowsCurrentUserProtectionAcceptance.Verify);
RunAcceptance(nameof(VaultKeyFoundationAcceptance), VaultKeyFoundationAcceptance.Verify);
RunAcceptance(nameof(VaultMetadataAcceptance), VaultMetadataAcceptance.Verify);
RunAcceptance(nameof(VaultBoundaryAcceptance), VaultBoundaryAcceptance.Verify);
RunAcceptance(nameof(VaultItemStoreAcceptance), VaultItemStoreAcceptance.Verify);
RunAcceptance(nameof(VaultUnlockLockRaceAcceptance), VaultUnlockLockRaceAcceptance.Verify);
RunAcceptance(nameof(VaultExportAcceptance), VaultExportAcceptance.Verify);
RunAcceptance(nameof(SecureDeleteFoundationAcceptance), SecureDeleteFoundationAcceptance.Run);

string root = Path.Combine(Path.GetTempPath(), "SentinelCryptoHarness", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    TestKeyProtector protector = new(3, RandomNumberGenerator.GetBytes(32));
    TestKeyProtector wrongProtector = new(3, RandomNumberGenerator.GetBytes(32));
    IReadOnlyList<IFileKeyProtector> keys = new IFileKeyProtector[] { protector };
    FileEncryptionService service = new(SentinelEncryptedContainerV1.MinimumChunkSize);

    string source = Path.Combine(root, "résumé-源.bin");
    byte[] original = PatternBytes(SentinelEncryptedContainerV1.MinimumChunkSize * 3 + 123);
    File.WriteAllBytes(source, original);
    string originalHash = Convert.ToHexString(SHA256.HashData(original));

    string encrypted = Path.Combine(root, "first.senc");
    FileEncryptionResult encryption = await service.EncryptAsync(source, encrypted, keys);
    Require(encryption.Succeeded && encryption.Verified, "Normal multi-chunk encryption was not verified: " + encryption.Code);
    Require(File.Exists(source), "Encryption removed the original plaintext.");
    Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) == originalHash, "Encryption modified the original plaintext.");

    FileContainerVerificationResult verified = await service.VerifyAsync(encrypted, keys);
    Require(verified.Succeeded, "Fresh encrypted container did not verify: " + verified.Code);
    Require(verified.ChunkCount == 4, $"Expected four chunks at 64 KiB, got {verified.ChunkCount}.");
    Require(verified.ChunkSize == SentinelEncryptedContainerV1.MinimumChunkSize, "Container did not preserve the declared chunk size.");

    string decrypted = Path.Combine(root, "decrypted.bin");
    FileDecryptionResult decryption = await service.DecryptAsync(encrypted, decrypted, keys);
    Require(decryption.Succeeded, "Normal decryption failed: " + decryption.Code);
    Require(File.ReadAllBytes(decrypted).SequenceEqual(original), "Decrypted plaintext did not exactly match the original.");

    string encryptedAgain = Path.Combine(root, "second.senc");
    Require((await service.EncryptAsync(source, encryptedAgain, keys)).Succeeded, "Second encryption failed.");
    byte[] firstBytes = File.ReadAllBytes(encrypted);
    byte[] secondBytes = File.ReadAllBytes(encryptedAgain);
    Require(!firstBytes.AsSpan(36, 8).SequenceEqual(secondBytes.AsSpan(36, 8)), "Two encryptions reused the per-container nonce prefix.");

    string empty = Path.Combine(root, "empty.txt");
    File.WriteAllBytes(empty, Array.Empty<byte>());
    string emptyEncrypted = Path.Combine(root, "empty.senc");
    Require((await service.EncryptAsync(empty, emptyEncrypted, keys)).Succeeded, "Empty-file encryption failed.");
    FileContainerVerificationResult emptyVerified = await service.VerifyAsync(emptyEncrypted, keys);
    Require(emptyVerified.Succeeded && emptyVerified.ChunkCount == 0 && emptyVerified.PlaintextBytes == 0, "Empty-file container metadata was invalid.");
    string emptyDecrypted = Path.Combine(root, "empty.out");
    Require((await service.DecryptAsync(emptyEncrypted, emptyDecrypted, keys)).Succeeded && new FileInfo(emptyDecrypted).Length == 0, "Empty-file decryption failed.");

    string readOnly = Path.Combine(root, "readonly.txt");
    File.WriteAllText(readOnly, "read-only input");
    File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);
    string readOnlyEncrypted = Path.Combine(root, "readonly.senc");
    Require((await service.EncryptAsync(readOnly, readOnlyEncrypted, keys)).Succeeded, "Read-only input could not be encrypted non-destructively.");
    File.SetAttributes(readOnly, FileAttributes.Normal);

    FileContainerVerificationResult wrongKey = await service.VerifyAsync(encrypted, new IFileKeyProtector[] { wrongProtector });
    Require(!wrongKey.Succeeded && wrongKey.Code == "KeyUnavailable", "Wrong key protector was not rejected as unavailable.");

    string corruptHeaderTag = Path.Combine(root, "corrupt-header-tag.senc");
    MutatedCopy(encrypted, corruptHeaderTag, bytes => bytes[HeaderLength(bytes)] ^= 0x40);
    Require(!(await service.VerifyAsync(corruptHeaderTag, keys)).Succeeded, "Corrupted header authentication tag was accepted.");

    string corruptCiphertext = Path.Combine(root, "corrupt-ciphertext.senc");
    MutatedCopy(encrypted, corruptCiphertext, bytes => bytes[DataOffset(bytes) + 8] ^= 0x01);
    Require(!(await service.VerifyAsync(corruptCiphertext, keys)).Succeeded, "Corrupted ciphertext was accepted.");

    string corruptChunkTag = Path.Combine(root, "corrupt-chunk-tag.senc");
    MutatedCopy(encrypted, corruptChunkTag, bytes =>
    {
        int offset = DataOffset(bytes);
        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
        bytes[offset + 8 + length] ^= 0x80;
    });
    Require(!(await service.VerifyAsync(corruptChunkTag, keys)).Succeeded, "Corrupted chunk authentication tag was accepted.");

    string truncated = Path.Combine(root, "truncated.senc");
    byte[] truncatedBytes = File.ReadAllBytes(encrypted)[..^5];
    File.WriteAllBytes(truncated, truncatedBytes);
    Require(!(await service.VerifyAsync(truncated, keys)).Succeeded, "Truncated container was accepted.");

    string trailing = Path.Combine(root, "trailing.senc");
    File.Copy(encrypted, trailing);
    await File.AppendAllTextAsync(trailing, "X");
    Require(!(await service.VerifyAsync(trailing, keys)).Succeeded, "Container with trailing data was accepted.");

    string badMagic = Path.Combine(root, "bad-magic.senc");
    MutatedCopy(encrypted, badMagic, bytes => bytes[0] ^= 0x20);
    FileContainerVerificationResult badMagicResult = await service.VerifyAsync(badMagic, keys);
    Require(!badMagicResult.Succeeded && badMagicResult.Code == "InvalidContainer", "Invalid container magic did not fail closed.");

    string collision = Path.Combine(root, "collision.senc");
    File.WriteAllText(collision, "preserve me");
    string collisionBefore = File.ReadAllText(collision);
    FileEncryptionResult collisionResult = await service.EncryptAsync(source, collision, keys);
    Require(!collisionResult.Succeeded && collisionResult.Code == "OutputCollision" && File.ReadAllText(collision) == collisionBefore,
        "Encryption output collision did not preserve the existing destination.");

    string decryptCollision = Path.Combine(root, "decrypt-collision.bin");
    File.WriteAllText(decryptCollision, "preserve plaintext destination");
    FileDecryptionResult decryptCollisionResult = await service.DecryptAsync(encrypted, decryptCollision, keys);
    Require(!decryptCollisionResult.Succeeded && decryptCollisionResult.Code == "OutputCollision",
        "Decryption output collision was not rejected.");

    FileEncryptionResult inPlace = await service.EncryptAsync(source, source, keys);
    Require(!inPlace.Succeeded && inPlace.Code == "InvalidPath", "In-place encryption was accepted.");

    string canceledOutput = Path.Combine(root, "canceled.senc");
    using CancellationTokenSource canceled = new();
    canceled.Cancel();
    FileEncryptionResult canceledResult = await service.EncryptAsync(source, canceledOutput, keys, canceled.Token);
    Require(!canceledResult.Succeeded && canceledResult.Code == "Canceled" && File.Exists(source), "Canceled encryption did not preserve the original.");
    Require(!File.Exists(canceledOutput) || canceledResult.InvalidOutputRemains, "Canceled encryption left an unreported partial output.");

    string corruptDecryptOutput = Path.Combine(root, "corrupt-decrypt.out");
    FileDecryptionResult corruptDecrypt = await service.DecryptAsync(corruptCiphertext, corruptDecryptOutput, keys);
    Require(!corruptDecrypt.Succeeded, "Authenticated decryption accepted corrupted ciphertext.");
    Require(!File.Exists(corruptDecryptOutput) || corruptDecrypt.InvalidOutputRemains, "Failed decryption left unreported plaintext output.");

    Console.WriteLine("Multi-chunk encrypt/verify/decrypt round trip: PASS");
    Console.WriteLine("Empty/Unicode/read-only source handling: PASS");
    Console.WriteLine("Nonce-prefix uniqueness across encryptions: PASS");
    Console.WriteLine("Wrong-key rejection: PASS");
    Console.WriteLine("Header/ciphertext/tag corruption rejection: PASS");
    Console.WriteLine("Truncation/trailing-data rejection: PASS");
    Console.WriteLine("Output collision / no in-place encryption: PASS");
    Console.WriteLine("Cancellation and failed-decryption cleanup semantics: PASS");
    Console.WriteLine("RESULT: PASS");
}
finally
{
    try
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
        }
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    catch { }
}

internal sealed class TestKeyProtector : IFileKeyProtector
{
    private const ushort WrappingAlgorithmAesGcm = 1;
    private readonly byte[] _masterKey;

    internal TestKeyProtector(ushort protectionModeId, byte[] masterKey)
    {
        ProtectionModeId = protectionModeId;
        _masterKey = masterKey.ToArray();
    }

    public ushort ProtectionModeId { get; }

    public ValueTask<WrappedFileKeyRecord> WrapAsync(ReadOnlyMemory<byte> dataEncryptionKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertext = new byte[dataEncryptionKey.Length];
        byte[] tag = new byte[16];
        using AesGcm aes = new(_masterKey, 16);
        aes.Encrypt(nonce, dataEncryptionKey.Span, ciphertext, tag, new byte[] { (byte)ProtectionModeId });
        byte[] wrapped = new byte[ciphertext.Length + tag.Length];
        ciphertext.CopyTo(wrapped, 0);
        tag.CopyTo(wrapped, ciphertext.Length);
        CryptographicOperations.ZeroMemory(ciphertext);
        CryptographicOperations.ZeroMemory(tag);
        return ValueTask.FromResult(new WrappedFileKeyRecord(1, ProtectionModeId, WrappingAlgorithmAesGcm, nonce, wrapped));
    }

    public ValueTask<byte[]?> TryUnwrapAsync(WrappedFileKeyRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record.ProtectionModeId != ProtectionModeId || record.WrappingAlgorithmId != WrappingAlgorithmAesGcm ||
            record.Parameters.Length != 12 || record.WrappedDek.Length != 48)
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        byte[] plaintext = new byte[32];
        try
        {
            using AesGcm aes = new(_masterKey, 16);
            aes.Decrypt(record.Parameters, record.WrappedDek.AsSpan(0, 32), record.WrappedDek.AsSpan(32, 16), plaintext,
                new byte[] { (byte)ProtectionModeId });
            return ValueTask.FromResult<byte[]?>(plaintext);
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return ValueTask.FromResult<byte[]?>(null);
        }
    }
}
