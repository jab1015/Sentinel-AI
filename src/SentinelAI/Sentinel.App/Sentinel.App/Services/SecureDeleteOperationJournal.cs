using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Sentinel.App.Services;

internal enum SecureDeleteOperationState
{
    Prepared = 0,
    IdentityVerified = 1,
    PrimaryMutationStarted = 2,
    PrimaryRemovalVerified = 3,
    RelatedCleanupPending = 4,
    Complete = 5,
    RecoveryRequired = 6
}

internal sealed record SecureDeleteOperationRecord(
    Guid OperationId,
    Guid AuthorizationId,
    SecureDeleteTargetIdentity Target,
    string VolumeRoot,
    string? FileSystem,
    SecureDeleteOperationState State,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string? Detail);

/// <summary>
/// Durable, non-destructive journal for Secure Delete operation state. This component only
/// persists transaction metadata under its own journal root; it never opens or mutates the
/// approved target file. Each record carries a Windows current-user protected digest so a
/// structurally valid edit is rejected unless its integrity proof also verifies. A later
/// executor must persist PrimaryMutationStarted successfully before any target mutation.
/// </summary>
internal sealed class SecureDeleteOperationJournal
{
    private const int SchemaVersion = 2;
    private readonly string _journalRoot;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly WindowsCurrentUserFileKeyProtector _integrityProtector = new();

    internal SecureDeleteOperationJournal(string journalRoot, Func<DateTimeOffset>? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(journalRoot))
            throw new ArgumentException("A journal root is required.", nameof(journalRoot));

        _journalRoot = Path.GetFullPath(journalRoot);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(_journalRoot);
    }

    internal SecureDeleteOperationRecord Begin(SecureDeleteAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (authorization.AuthorizationId == Guid.Empty || authorization.Target.IsEmpty)
            throw new InvalidOperationException("A valid Secure Delete authorization is required.");
        if (string.IsNullOrWhiteSpace(authorization.VolumeRoot))
            throw new InvalidOperationException("The authorization has no storage boundary.");

        DateTimeOffset now = _utcNow();
        SecureDeleteOperationRecord record = new(
            Guid.NewGuid(),
            authorization.AuthorizationId,
            authorization.Target,
            authorization.VolumeRoot,
            authorization.FileSystem,
            SecureDeleteOperationState.Prepared,
            now,
            now,
            null);

        Persist(record, requireNew: true);
        return record;
    }

    internal SecureDeleteOperationRecord Advance(
        SecureDeleteOperationRecord current,
        SecureDeleteOperationState nextState,
        string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        SecureDeleteOperationRecord persisted = ReadRequired(current.OperationId);
        EnsureSameOperation(current, persisted);

        if (!IsAllowedTransition(persisted.State, nextState))
            throw new InvalidOperationException($"Secure Delete journal transition {persisted.State} -> {nextState} is not allowed.");

        SecureDeleteOperationRecord next = persisted with
        {
            State = nextState,
            UpdatedUtc = _utcNow(),
            Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim()
        };

        Persist(next, requireNew: false);
        return next;
    }

    internal bool TryRead(Guid operationId, out SecureDeleteOperationRecord? record)
    {
        record = null;
        if (operationId == Guid.Empty) return false;
        string path = GetRecordPath(operationId);
        if (!File.Exists(path)) return false;

        try
        {
            JournalEnvelope? envelope = JsonSerializer.Deserialize<JournalEnvelope>(File.ReadAllText(path, Encoding.UTF8));
            if (!VerifyEnvelope(envelope, operationId))
                return false;
            record = envelope!.Record;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    internal SecureDeleteOperationRecord ReadRequired(Guid operationId)
    {
        if (!TryRead(operationId, out SecureDeleteOperationRecord? record) || record is null)
            throw new InvalidOperationException("Secure Delete journal state was unavailable, invalid, or tampered.");
        return record;
    }

    private void Persist(SecureDeleteOperationRecord record, bool requireNew)
    {
        if (!IsValidRecord(record, record.OperationId))
            throw new InvalidOperationException("Refusing to persist incomplete or invalid Secure Delete journal state.");

        string finalPath = GetRecordPath(record.OperationId);
        if (requireNew && File.Exists(finalPath))
            throw new IOException("Secure Delete journal operation already exists.");

        string tempPath = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
        WrappedFileKeyRecord integrityProof = CreateIntegrityProof(record);
        JournalEnvelope envelope = new(SchemaVersion, record, integrityProof);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions { WriteIndented = true });

        try
        {
            using (FileStream stream = new(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, finalPath, overwrite: !requireNew);

            using FileStream verifyStream = new(
                finalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            JournalEnvelope? verify = JsonSerializer.Deserialize<JournalEnvelope>(verifyStream);
            if (!VerifyEnvelope(verify, record.OperationId) || verify!.Record != record)
                throw new IOException("Secure Delete journal persistence verification failed.");
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup of the journal's own temporary file only.
            }
        }
    }

    private WrappedFileKeyRecord CreateIntegrityProof(SecureDeleteOperationRecord record)
    {
        byte[] digest = ComputeRecordDigest(record);
        try
        {
            return _integrityProtector.WrapAsync(digest, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private bool VerifyEnvelope(JournalEnvelope? envelope, Guid expectedOperationId)
    {
        if (envelope is null || envelope.SchemaVersion != SchemaVersion || envelope.Record is null ||
            envelope.IntegrityProof is null || !IsValidRecord(envelope.Record, expectedOperationId))
        {
            return false;
        }

        byte[] expectedDigest = ComputeRecordDigest(envelope.Record);
        byte[]? protectedDigest = null;
        try
        {
            protectedDigest = _integrityProtector.TryUnwrapAsync(envelope.IntegrityProof, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            return protectedDigest is { Length: 32 } &&
                   CryptographicOperations.FixedTimeEquals(expectedDigest, protectedDigest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedDigest);
            if (protectedDigest is not null)
                CryptographicOperations.ZeroMemory(protectedDigest);
        }
    }

    private static byte[] ComputeRecordDigest(SecureDeleteOperationRecord record) =>
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record));

    private string GetRecordPath(Guid operationId) =>
        Path.Combine(_journalRoot, operationId.ToString("N") + ".json");

    private static bool IsValidRecord(SecureDeleteOperationRecord record, Guid expectedOperationId)
    {
        if (record.OperationId == Guid.Empty || record.OperationId != expectedOperationId ||
            record.AuthorizationId == Guid.Empty || record.Target.IsEmpty ||
            string.IsNullOrWhiteSpace(record.VolumeRoot) ||
            !IsKnownState(record.State) ||
            record.CreatedUtc == default || record.UpdatedUtc == default ||
            record.UpdatedUtc < record.CreatedUtc)
        {
            return false;
        }

        return true;
    }

    private static bool IsKnownState(SecureDeleteOperationState state) => state switch
    {
        SecureDeleteOperationState.Prepared => true,
        SecureDeleteOperationState.IdentityVerified => true,
        SecureDeleteOperationState.PrimaryMutationStarted => true,
        SecureDeleteOperationState.PrimaryRemovalVerified => true,
        SecureDeleteOperationState.RelatedCleanupPending => true,
        SecureDeleteOperationState.Complete => true,
        SecureDeleteOperationState.RecoveryRequired => true,
        _ => false
    };

    private static void EnsureSameOperation(
        SecureDeleteOperationRecord expected,
        SecureDeleteOperationRecord persisted)
    {
        if (expected.OperationId != persisted.OperationId ||
            expected.AuthorizationId != persisted.AuthorizationId ||
            expected.Target != persisted.Target ||
            !string.Equals(expected.VolumeRoot, persisted.VolumeRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.FileSystem, persisted.FileSystem, StringComparison.OrdinalIgnoreCase) ||
            expected.State != persisted.State ||
            expected.CreatedUtc != persisted.CreatedUtc)
        {
            throw new InvalidOperationException("Secure Delete journal state changed unexpectedly.");
        }
    }

    private static bool IsAllowedTransition(SecureDeleteOperationState current, SecureDeleteOperationState next)
    {
        if (next == SecureDeleteOperationState.RecoveryRequired)
            return current != SecureDeleteOperationState.Complete && current != SecureDeleteOperationState.RecoveryRequired;

        return (current, next) switch
        {
            (SecureDeleteOperationState.Prepared, SecureDeleteOperationState.IdentityVerified) => true,
            (SecureDeleteOperationState.IdentityVerified, SecureDeleteOperationState.PrimaryMutationStarted) => true,
            (SecureDeleteOperationState.PrimaryMutationStarted, SecureDeleteOperationState.PrimaryRemovalVerified) => true,
            (SecureDeleteOperationState.PrimaryRemovalVerified, SecureDeleteOperationState.RelatedCleanupPending) => true,
            (SecureDeleteOperationState.RelatedCleanupPending, SecureDeleteOperationState.Complete) => true,
            _ => false
        };
    }

    private sealed record JournalEnvelope(
        int SchemaVersion,
        SecureDeleteOperationRecord Record,
        WrappedFileKeyRecord IntegrityProof);
}
