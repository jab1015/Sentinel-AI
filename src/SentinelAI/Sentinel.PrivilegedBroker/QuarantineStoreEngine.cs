using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed class QuarantineStoreEngine
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint FileListDirectory = 0x0001;
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint CreateNew = 1;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int FileDispositionInfoClass = 4;
    private const int FileRenameInfoClass = 3;

    private readonly string _root;
    private readonly string _storeRoot;
    private readonly string _recordsRoot;
    private readonly string _transactionRoot;
    private readonly string _healthPath;
    private readonly Func<string, bool> _isProtectedDestination;
    private readonly Action<string> _makePrivate;
    private readonly Action<string> _restoreInheritedAcl;
    private readonly Action<QuarantineCheckpoint>? _checkpoint;

    internal QuarantineStoreEngine(
        string root,
        Func<string, bool> isProtectedDestination,
        Action<string> makePrivate,
        Action<string> restoreInheritedAcl,
        Action<QuarantineCheckpoint>? checkpoint = null)
    {
        _root = Path.GetFullPath(root);
        _storeRoot = Path.Combine(_root, "QuarantineStore");
        _recordsRoot = Path.Combine(_root, "QuarantineRecords");
        _transactionRoot = Path.Combine(_root, "Transactions");
        _healthPath = Path.Combine(_recordsRoot, "quarantine-health.txt");
        _isProtectedDestination = isProtectedDestination;
        _makePrivate = makePrivate;
        _restoreInheritedAcl = restoreInheritedAcl;
        _checkpoint = checkpoint;
    }

    internal void EnsureDirectories()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_storeRoot);
        Directory.CreateDirectory(_recordsRoot);
        Directory.CreateDirectory(_transactionRoot);
    }

    internal QuarantineStoreResult Quarantine(string? rawItemId, string? rawSourcePath)
    {
        if (!TryValidateItemId(rawItemId, out string itemId))
            return Fail("InvalidItemId", "The quarantine item ID is invalid.");
        if (!TryCanonicalizeExistingFile(rawSourcePath, out string sourcePath, out string sourceError))
            return Fail("InvalidSource", sourceError);
        if (_isProtectedDestination(sourcePath))
            return Fail("ProtectedSource", "Protected Windows and Program Files locations are not eligible for direct Sentinel quarantine.");

        string payloadPath = PayloadPath(itemId);
        string recordPath = RecordPath(itemId);
        string transactionPath = TransactionPath(itemId);
        string tempPayload = TempPayloadPath(itemId);
        if (File.Exists(payloadPath) || File.Exists(recordPath) || File.Exists(transactionPath) || File.Exists(tempPayload))
            return Fail("ItemAlreadyExists", "The quarantine item ID is already in use.");

        using FileStream source = OpenStableFile(sourcePath, FileAccess.Read, requireSingleLink: true, out StableFileIdentity sourceIdentity, out string openError);
        if (!string.IsNullOrEmpty(openError)) return Fail("UnsafeSource", openError);
        if (!PathsEqual(sourceIdentity.FinalPath, sourcePath))
            return Fail("SourceIdentityMismatch", "The source path resolved to a different filesystem object and was rejected.");

        string hash = HashOpenStream(source);
        QuarantineTransaction txn = new(
            itemId,
            "Quarantine",
            "Prepared",
            sourcePath,
            tempPayload,
            hash,
            DateTimeOffset.UtcNow);
        WriteAtomicJson(transactionPath, txn, overwrite: false);
        Hit(QuarantineCheckpoint.QuarantineIntentPersisted);

        try
        {
            source.Position = 0;
            using (FileStream temp = new(tempPayload, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                source.CopyTo(temp);
                temp.Flush(true);
                temp.Position = 0;
                string copiedHash = Convert.ToHexString(SHA256.HashData(temp));
                if (!hash.Equals(copiedHash, StringComparison.OrdinalIgnoreCase))
                    return Fail("CopyVerificationFailed", "The protected quarantine copy did not match the source file.");
            }
            _makePrivate(tempPayload);
            Hit(QuarantineCheckpoint.QuarantineTemporaryCopyReady);

            File.Move(tempPayload, payloadPath, overwrite: false);
            txn = txn with { Stage = "PayloadReady" };
            WriteAtomicJson(transactionPath, txn, overwrite: true);
            Hit(QuarantineCheckpoint.QuarantinePayloadReady);

            if (!TryMarkOpenFileForDeletion(source.SafeFileHandle, out string deleteError))
                return Fail("SourceDeleteFailed", deleteError);
        }
        finally
        {
            // The source handle is deliberately kept open through copy + delete disposition.
            // Its FileShare mode blocks write/rename substitution while the operation is in flight.
        }

        source.Dispose();
        Hit(QuarantineCheckpoint.QuarantineSourceDeleted);

        if (File.Exists(sourcePath))
            return Fail("ContainmentVerificationFailed", "The original source path still exists after the exact source object was marked for deletion.");
        if (!TryVerifyExactPayload(payloadPath, hash, out string payloadError))
            return Fail("ContainmentVerificationFailed", payloadError);

        ProtectedQuarantineRecord record = new(itemId, sourcePath, hash, DateTimeOffset.UtcNow);
        WriteAtomicJson(recordPath, record, overwrite: false);
        Hit(QuarantineCheckpoint.QuarantineRecordCommitted);
        SafeDeleteTransaction(transactionPath);
        RefreshHealthReport();
        return Ok(itemId, hash, "The file was moved into the protected quarantine store and verified by exact file identity and SHA-256.");
    }

    internal QuarantineStoreResult Restore(string? rawItemId)
    {
        if (!TryValidateItemId(rawItemId, out string itemId))
            return Fail("InvalidItemId", "The quarantine item ID is invalid.");

        RecordReadResult read = ReadRecord(itemId);
        if (read.Status == RecordStatus.Missing)
            return Fail("RecordMissing", "The protected quarantine record was not found.");
        if (read.Status == RecordStatus.Corrupt || read.Record is null)
            return Fail("RecordCorrupt", "The protected quarantine record is corrupt. Sentinel preserved it for investigation and did not restore anything.");

        ProtectedQuarantineRecord record = read.Record;
        string payloadPath = PayloadPath(itemId);
        if (!File.Exists(payloadPath)) return Fail("PayloadMissing", "The quarantined payload was not found.");

        using FileStream payload = OpenStableFile(payloadPath, FileAccess.Read, requireSingleLink: true, out StableFileIdentity payloadIdentity, out string payloadOpenError);
        if (!string.IsNullOrEmpty(payloadOpenError)) return Fail("UnsafePayload", payloadOpenError);
        if (!PathsEqual(payloadIdentity.FinalPath, payloadPath))
            return Fail("PayloadIdentityMismatch", "The protected payload resolved outside its expected store path.");
        string payloadHash = HashOpenStream(payload);
        if (!payloadHash.Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
            return Fail("PayloadTampered", "The quarantined payload no longer matches its protected record.");

        if (!TryCanonicalizeDestination(record.OriginalPath, out string destination, out string destinationError))
            return Fail("InvalidDestination", destinationError);
        if (_isProtectedDestination(destination))
            return Fail("ProtectedDestination", "Sentinel will not restore a quarantine item into a protected Windows or Program Files location through this broker version.");
        if (File.Exists(destination))
            return Fail("RestoreCollision", "A file already exists at the original location. Sentinel will not overwrite it.");

        string? parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            return Fail("InvalidDestination", "The original parent directory no longer exists. Sentinel will not create a new restore path implicitly.");

        using DirectoryLease lease = DirectoryLease.Acquire(parent);
        if (!lease.Succeeded)
            return Fail("UnsafeDestination", lease.Error);
        if (File.Exists(destination))
            return Fail("RestoreCollision", "A file appeared at the original location while Sentinel was preparing the restore. Sentinel did not overwrite it.");

        string tempDestination = Path.Combine(parent, $".{Path.GetFileName(destination)}.sentinel-restore-{itemId}-{Guid.NewGuid():N}.tmp");
        QuarantineTransaction txn = new(itemId, "Restore", "Prepared", destination, tempDestination, record.Sha256, DateTimeOffset.UtcNow);
        WriteAtomicJson(TransactionPath(itemId), txn, overwrite: false);
        Hit(QuarantineCheckpoint.RestoreIntentPersisted);

        using SafeFileHandle tempHandle = CreateFileW(
            tempDestination,
            GenericRead | GenericWrite | DeleteAccess,
            FileShareRead,
            IntPtr.Zero,
            CreateNew,
            FileAttributeNormal,
            IntPtr.Zero);
        if (tempHandle.IsInvalid)
            return Fail("RestoreTempCreateFailed", Win32("Sentinel could not create the protected temporary restore file."));

        using FileStream temp = new(tempHandle, FileAccess.ReadWrite, 81920, isAsync: false);
        payload.Position = 0;
        payload.CopyTo(temp);
        temp.Flush(true);
        temp.Position = 0;
        string restoredHash = Convert.ToHexString(SHA256.HashData(temp));
        if (!restoredHash.Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
            return Fail("RestoreCopyVerificationFailed", "The temporary restored copy did not match the quarantine record.");
        _makePrivate(tempDestination);

        txn = txn with { Stage = "TempReady" };
        WriteAtomicJson(TransactionPath(itemId), txn, overwrite: true);
        Hit(QuarantineCheckpoint.RestoreTemporaryCopyReady);

        if (!TryRenameOpenFile(temp.SafeFileHandle, lease.ParentHandle, Path.GetFileName(destination), out string renameError))
            return Fail("RestoreRenameFailed", renameError);

        txn = txn with { Stage = "DestinationReady" };
        WriteAtomicJson(TransactionPath(itemId), txn, overwrite: true);
        Hit(QuarantineCheckpoint.RestoreDestinationReady);

        string resolvedDestination = GetFinalPath(temp.SafeFileHandle);
        if (!PathsEqual(resolvedDestination, destination))
            return Fail("RestoreIdentityMismatch", "The restored object resolved to an unexpected path. Recovery state was preserved.");
        temp.Position = 0;
        restoredHash = Convert.ToHexString(SHA256.HashData(temp));
        if (!restoredHash.Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
            return Fail("RestoreVerificationFailed", "The restored object did not match the protected payload after the atomic rename.");

        _restoreInheritedAcl(destination);
        payload.Dispose();
        if (!TryDeleteExactPath(payloadPath, requireSingleLink: true, expectedSha256: record.Sha256, out string payloadDeleteError))
            return Fail("PayloadDeleteFailed", payloadDeleteError);
        Hit(QuarantineCheckpoint.RestorePayloadDeleted);

        SafeDeleteRecord(RecordPath(itemId));
        SafeDeleteTransaction(TransactionPath(itemId));
        RefreshHealthReport();
        return Ok(itemId, record.Sha256, "The exact verified quarantine payload was restored without overwriting an existing file, and the protected copy was removed.");
    }

    internal QuarantineStoreResult Delete(string? rawItemId)
    {
        if (!TryValidateItemId(rawItemId, out string itemId))
            return Fail("InvalidItemId", "The quarantine item ID is invalid.");
        RecordReadResult read = ReadRecord(itemId);
        if (read.Status == RecordStatus.Missing)
            return Fail("RecordMissing", "The protected quarantine record was not found.");
        if (read.Status == RecordStatus.Corrupt || read.Record is null)
            return Fail("RecordCorrupt", "The protected quarantine record is corrupt. Sentinel preserved it for investigation and did not delete anything.");

        ProtectedQuarantineRecord record = read.Record;
        string payloadPath = PayloadPath(itemId);
        if (!File.Exists(payloadPath)) return Fail("PayloadMissing", "The quarantined payload was not found.");
        if (!TryVerifyExactPayload(payloadPath, record.Sha256, out string verifyError))
            return Fail("PayloadTampered", verifyError);

        WriteAtomicJson(TransactionPath(itemId), new QuarantineTransaction(
            itemId, "Delete", "Prepared", record.OriginalPath, string.Empty, record.Sha256, DateTimeOffset.UtcNow), overwrite: false);
        Hit(QuarantineCheckpoint.DeleteIntentPersisted);

        if (!TryDeleteExactPath(payloadPath, requireSingleLink: true, expectedSha256: record.Sha256, out string deleteError))
            return Fail("DeleteVerificationFailed", deleteError);
        Hit(QuarantineCheckpoint.DeletePayloadDeleted);

        SafeDeleteRecord(RecordPath(itemId));
        SafeDeleteTransaction(TransactionPath(itemId));
        RefreshHealthReport();
        return Ok(itemId, record.Sha256, "The exact protected quarantine payload was permanently deleted and verified absent.");
    }

    internal IReadOnlyList<QuarantineStoreIssue> Recover()
    {
        EnsureDirectories();
        IReadOnlyList<QuarantineStoreIssue> guardIssues = QuarantineRecoveryGuard.Validate(_root);
        if (guardIssues.Count > 0)
        {
            RefreshHealthReport(guardIssues);
            return guardIssues;
        }

        List<QuarantineStoreIssue> issues = new();
        foreach (string transactionPath in Directory.EnumerateFiles(_transactionRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            QuarantineTransaction? txn;
            try { txn = JsonSerializer.Deserialize<QuarantineTransaction>(File.ReadAllText(transactionPath)); }
            catch
            {
                issues.Add(new("CorruptTransaction", transactionPath, "A quarantine transaction could not be parsed and was preserved."));
                continue;
            }
            if (txn is null || !TryValidateItemId(txn.ItemId, out string itemId) || !IsValidHash(txn.Sha256))
            {
                issues.Add(new("CorruptTransaction", transactionPath, "A quarantine transaction failed structural validation and was preserved."));
                continue;
            }

            try
            {
                RecoverTransaction(txn, transactionPath, itemId, issues);
            }
            catch (Exception ex)
            {
                issues.Add(new("RecoveryFailed", transactionPath, $"Recovery failed closed ({ex.GetType().Name}); transaction state was preserved."));
            }
        }

        foreach (string recordPath in Directory.EnumerateFiles(_recordsRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            string id = Path.GetFileNameWithoutExtension(recordPath);
            if (!TryValidateItemId(id, out string itemId))
            {
                issues.Add(new("UnexpectedRecord", recordPath, "An unexpected record file was preserved."));
                continue;
            }
            if (ReadRecord(itemId).Status == RecordStatus.Corrupt)
                issues.Add(new("CorruptRecord", recordPath, "A protected quarantine record is corrupt and was preserved rather than ignored."));
        }

        foreach (string payloadPath in Directory.EnumerateFiles(_storeRoot, "*.sentinelq", SearchOption.TopDirectoryOnly))
        {
            string id = Path.GetFileNameWithoutExtension(payloadPath);
            if (!TryValidateItemId(id, out string itemId))
            {
                issues.Add(new("UnexpectedPayload", payloadPath, "An unexpected protected-store payload was preserved."));
                continue;
            }
            if (!File.Exists(RecordPath(itemId)) && !File.Exists(TransactionPath(itemId)))
                issues.Add(new("OrphanPayload", payloadPath, "An orphaned protected payload has no record or transaction. Sentinel preserved it for recovery."));
        }

        RefreshHealthReport(issues);
        return issues;
    }

    internal QuarantineStoreSnapshot Inspect()
    {
        List<ProtectedQuarantineRecord> records = new();
        List<QuarantineStoreIssue> issues = new();
        foreach (string recordPath in Directory.EnumerateFiles(_recordsRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            string id = Path.GetFileNameWithoutExtension(recordPath);
            if (!TryValidateItemId(id, out string itemId))
            {
                issues.Add(new("UnexpectedRecord", recordPath, "Unexpected protected record file."));
                continue;
            }
            RecordReadResult read = ReadRecord(itemId);
            if (read.Status == RecordStatus.Valid && read.Record is not null) records.Add(read.Record);
            else if (read.Status == RecordStatus.Corrupt) issues.Add(new("CorruptRecord", recordPath, "Protected record is corrupt."));
        }
        foreach (string payloadPath in Directory.EnumerateFiles(_storeRoot, "*.sentinelq", SearchOption.TopDirectoryOnly))
        {
            string id = Path.GetFileNameWithoutExtension(payloadPath);
            if (TryValidateItemId(id, out string itemId) && !File.Exists(RecordPath(itemId)) && !File.Exists(TransactionPath(itemId)))
                issues.Add(new("OrphanPayload", payloadPath, "Protected payload is orphaned."));
        }
        return new(records, issues);
    }

    internal string PayloadPathForTest(string itemId) => PayloadPath(itemId);
    internal string RecordPathForTest(string itemId) => RecordPath(itemId);
    internal string TransactionPathForTest(string itemId) => TransactionPath(itemId);

    private void RecoverTransaction(QuarantineTransaction txn, string transactionPath, string itemId, List<QuarantineStoreIssue> issues)
    {
        string payloadPath = PayloadPath(itemId);
        string recordPath = RecordPath(itemId);
        if (txn.Operation == "Quarantine")
        {
            bool sourceExists = File.Exists(txn.Path);
            bool payloadExists = File.Exists(payloadPath);
            bool tempExists = !string.IsNullOrWhiteSpace(txn.TempPath) && File.Exists(txn.TempPath);

            if (payloadExists && !sourceExists && TryVerifyExactPayload(payloadPath, txn.Sha256, out _))
            {
                RecordReadResult existing = ReadRecord(itemId);
                if (existing.Status == RecordStatus.Corrupt)
                {
                    issues.Add(new("CorruptRecord", recordPath, "Recovery found a corrupt protected record and preserved all state."));
                    return;
                }
                if (existing.Status == RecordStatus.Missing)
                    WriteAtomicJson(recordPath, new ProtectedQuarantineRecord(itemId, txn.Path, txn.Sha256, txn.CreatedAtUtc), overwrite: false);
                SafeDeleteBoundedTemp(txn.TempPath, itemId);
                SafeDeleteTransaction(transactionPath);
                return;
            }

            if (sourceExists)
            {
                if (payloadExists && TryVerifyExactPayload(payloadPath, txn.Sha256, out _))
                    TryDeleteExactPath(payloadPath, requireSingleLink: true, expectedSha256: txn.Sha256, out _);
                SafeDeleteBoundedTemp(txn.TempPath, itemId);
                SafeDeleteTransaction(transactionPath);
                return;
            }

            if (!payloadExists && !tempExists)
            {
                issues.Add(new("AmbiguousQuarantine", transactionPath, "The source and protected payload are both absent. Transaction state was preserved."));
                return;
            }

            issues.Add(new("IncompleteQuarantine", transactionPath, "Quarantine recovery could not prove a safe commit or rollback. State was preserved."));
            return;
        }

        if (txn.Operation == "Restore")
        {
            bool destinationExists = File.Exists(txn.Path);
            bool payloadExists = File.Exists(payloadPath);
            bool tempExists = !string.IsNullOrWhiteSpace(txn.TempPath) && File.Exists(txn.TempPath);

            if (destinationExists && VerifyPathHash(txn.Path, txn.Sha256))
            {
                SafeDeleteBoundedTemp(txn.TempPath, itemId);
                if (payloadExists)
                {
                    if (!TryDeleteExactPath(payloadPath, requireSingleLink: true, expectedSha256: txn.Sha256, out string deleteError))
                    {
                        issues.Add(new("RestoreRecoveryFailed", transactionPath, deleteError));
                        return;
                    }
                }
                _restoreInheritedAcl(txn.Path);
                SafeDeleteRecord(recordPath);
                SafeDeleteTransaction(transactionPath);
                return;
            }

            if (!destinationExists && payloadExists)
            {
                SafeDeleteBoundedTemp(txn.TempPath, itemId);
                SafeDeleteTransaction(transactionPath);
                return;
            }

            if (tempExists && payloadExists)
            {
                SafeDeleteBoundedTemp(txn.TempPath, itemId);
                SafeDeleteTransaction(transactionPath);
                return;
            }

            issues.Add(new("IncompleteRestore", transactionPath, "Restore recovery could not prove a safe commit or rollback. State was preserved."));
            return;
        }

        if (txn.Operation == "Delete")
        {
            RecordReadResult record = ReadRecord(itemId);
            if (!File.Exists(payloadPath))
            {
                SafeDeleteRecord(recordPath);
                SafeDeleteTransaction(transactionPath);
                return;
            }
            if (record.Status == RecordStatus.Valid && TryDeleteExactPath(payloadPath, requireSingleLink: true, expectedSha256: txn.Sha256, out _))
            {
                SafeDeleteRecord(recordPath);
                SafeDeleteTransaction(transactionPath);
                return;
            }
            issues.Add(new("IncompleteDelete", transactionPath, "Delete recovery could not verify the protected payload identity. State was preserved."));
            return;
        }

        issues.Add(new("UnknownTransaction", transactionPath, "Unknown quarantine transaction operation was preserved."));
    }

    private bool TryCanonicalizeExistingFile(string? raw, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) { error = "The source path is empty."; return false; }
        try { fullPath = Path.GetFullPath(raw); }
        catch { error = "The source path is invalid."; return false; }
        if (!File.Exists(fullPath)) { error = "The source file does not exist."; return false; }
        try
        {
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                error = "The source file is a reparse point and was rejected.";
                return false;
            }
        }
        catch { error = "The source file attributes could not be verified."; return false; }
        string? parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent) || HasReparsePointInExistingPath(parent))
        {
            error = "The source path contains a junction or symbolic link and was rejected.";
            return false;
        }
        return true;
    }

    private static bool TryCanonicalizeDestination(string raw, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) { error = "The restore destination is empty."; return false; }
        try
        {
            fullPath = Path.GetFullPath(raw);
            if (!Path.IsPathFullyQualified(fullPath)) { error = "The restore destination is not fully qualified."; return false; }
            return true;
        }
        catch { error = "The restore destination is invalid."; return false; }
    }

    private static bool HasReparsePointInExistingPath(string path)
    {
        DirectoryInfo? current = new(Path.GetFullPath(path));
        while (current is not null && current.Exists)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            current = current.Parent;
        }
        return false;
    }

    private static FileStream OpenStableFile(
        string path,
        FileAccess access,
        bool requireSingleLink,
        out StableFileIdentity identity,
        out string error)
    {
        identity = default;
        error = string.Empty;
        uint desired = GenericRead | DeleteAccess;
        if ((access & FileAccess.Write) != 0) desired |= GenericWrite;
        SafeFileHandle handle = CreateFileW(path, desired, FileShareRead, IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            error = Win32("Sentinel could not lock the exact file identity for the operation.");
            handle.Dispose();
            return new FileStream(new SafeFileHandle(IntPtr.Zero, ownsHandle: false), FileAccess.Read);
        }
        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation info))
        {
            error = Win32("Sentinel could not read the exact file identity.");
            handle.Dispose();
            return new FileStream(new SafeFileHandle(IntPtr.Zero, ownsHandle: false), FileAccess.Read);
        }
        if ((info.FileAttributes & FileAttributeReparsePoint) != 0)
        {
            error = "The file itself is a reparse point and was rejected.";
            handle.Dispose();
            return new FileStream(new SafeFileHandle(IntPtr.Zero, ownsHandle: false), FileAccess.Read);
        }
        if (requireSingleLink && info.NumberOfLinks != 1)
        {
            error = "The file has multiple hard links. Sentinel refused the operation because removing one path would not contain the same file identity everywhere.";
            handle.Dispose();
            return new FileStream(new SafeFileHandle(IntPtr.Zero, ownsHandle: false), FileAccess.Read);
        }
        string finalPath;
        try { finalPath = GetFinalPath(handle); }
        catch
        {
            error = "Sentinel could not resolve the file handle to a final path.";
            handle.Dispose();
            return new FileStream(new SafeFileHandle(IntPtr.Zero, ownsHandle: false), FileAccess.Read);
        }
        identity = new(finalPath, info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow, info.NumberOfLinks);
        return new FileStream(handle, access, 81920, isAsync: false);
    }

    private static string HashOpenStream(FileStream stream)
    {
        stream.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        stream.Position = 0;
        return hash;
    }

    private static bool TryMarkOpenFileForDeletion(SafeFileHandle handle, out string error)
    {
        FileDispositionInfo disposition = new() { DeleteFile = true };
        if (!SetFileInformationByHandle(handle, FileDispositionInfoClass, ref disposition, (uint)Marshal.SizeOf<FileDispositionInfo>()))
        {
            error = Win32("Windows refused exact-handle deletion of the source file.");
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryRenameOpenFile(SafeFileHandle fileHandle, SafeFileHandle parentHandle, string fileName, out string error)
    {
        byte[] nameBytes = Encoding.Unicode.GetBytes(fileName);
        int rootOffset = IntPtr.Size == 8 ? 8 : 4;
        int lengthOffset = rootOffset + IntPtr.Size;
        int nameOffset = lengthOffset + sizeof(int);
        IntPtr buffer = Marshal.AllocHGlobal(nameOffset + nameBytes.Length);
        try
        {
            Span<byte> zeros = new byte[nameOffset + nameBytes.Length];
            Marshal.Copy(zeros.ToArray(), 0, buffer, zeros.Length);
            Marshal.WriteByte(buffer, 0, 0);
            Marshal.WriteIntPtr(buffer, rootOffset, parentHandle.DangerousGetHandle());
            Marshal.WriteInt32(buffer, lengthOffset, nameBytes.Length);
            Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, nameOffset), nameBytes.Length);
            if (!SetFileInformationByHandle(fileHandle, FileRenameInfoClass, buffer, (uint)(nameOffset + nameBytes.Length)))
            {
                error = Win32("Windows refused the atomic exact-handle restore rename.");
                return false;
            }
            error = string.Empty;
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool TryDeleteExactPath(string path, bool requireSingleLink, string expectedSha256, out string error)
    {
        if (!File.Exists(path)) { error = string.Empty; return true; }
        using FileStream file = OpenStableFile(path, FileAccess.Read, requireSingleLink, out StableFileIdentity identity, out string openError);
        if (!string.IsNullOrEmpty(openError)) { error = openError; return false; }
        if (!PathsEqual(identity.FinalPath, path)) { error = "The file path resolved to a different filesystem object."; return false; }
        string hash = HashOpenStream(file);
        if (!hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) { error = "The file content no longer matches the protected record."; return false; }
        if (!TryMarkOpenFileForDeletion(file.SafeFileHandle, out error)) return false;
        file.Dispose();
        if (File.Exists(path)) { error = "The exact file remained present after deletion."; return false; }
        error = string.Empty;
        return true;
    }

    private static bool TryVerifyExactPayload(string path, string expectedSha256, out string error)
    {
        if (!File.Exists(path)) { error = "The protected payload is missing."; return false; }
        using FileStream payload = OpenStableFile(path, FileAccess.Read, requireSingleLink: true, out StableFileIdentity identity, out string openError);
        if (!string.IsNullOrEmpty(openError)) { error = openError; return false; }
        if (!PathsEqual(identity.FinalPath, path)) { error = "The protected payload resolved outside its expected path."; return false; }
        string hash = HashOpenStream(payload);
        if (!hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) { error = "The protected payload hash no longer matches its record."; return false; }
        error = string.Empty;
        return true;
    }

    private static bool VerifyPathHash(string path, string expectedSha256)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private RecordReadResult ReadRecord(string itemId)
    {
        string path = RecordPath(itemId);
        if (!File.Exists(path)) return new(RecordStatus.Missing, null);
        try
        {
            ProtectedQuarantineRecord? record = JsonSerializer.Deserialize<ProtectedQuarantineRecord>(File.ReadAllText(path));
            if (record is null || !record.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase) || !IsValidHash(record.Sha256))
                return new(RecordStatus.Corrupt, null);
            string original = Path.GetFullPath(record.OriginalPath);
            if (!Path.IsPathFullyQualified(original)) return new(RecordStatus.Corrupt, null);
            return new(RecordStatus.Valid, record with { OriginalPath = original });
        }
        catch { return new(RecordStatus.Corrupt, null); }
    }

    private void WriteAtomicJson<T>(string path, T value, bool overwrite)
    {
        string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value), new UTF8Encoding(false));
            using (FileStream stream = new(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) stream.Flush(true);
            File.Move(temp, path, overwrite);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private void SafeDeleteBoundedTemp(string? tempPath, string itemId)
    {
        if (string.IsNullOrWhiteSpace(tempPath) || !File.Exists(tempPath)) return;
        string full;
        try { full = Path.GetFullPath(tempPath); } catch { return; }
        string fileName = Path.GetFileName(full);
        bool protectedTemp = PathsEqual(Path.GetDirectoryName(full) ?? string.Empty, _storeRoot) &&
                             fileName.Equals(itemId + ".tmp", StringComparison.OrdinalIgnoreCase);
        bool restoreTemp = fileName.Contains(".sentinel-restore-" + itemId + "-", StringComparison.OrdinalIgnoreCase) &&
                           fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
        if (!protectedTemp && !restoreTemp) return;
        string? parent = Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(parent) || HasReparsePointInExistingPath(parent)) return;
        try { File.Delete(full); } catch { }
    }

    private void RefreshHealthReport() => RefreshHealthReport(Inspect().Issues);

    private void RefreshHealthReport(IReadOnlyList<QuarantineStoreIssue> issues)
    {
        try
        {
            if (issues.Count == 0)
            {
                if (File.Exists(_healthPath)) File.Delete(_healthPath);
                return;
            }
            string text = string.Join(Environment.NewLine, issues.Select(i => $"{i.Code}: {i.Message} [{i.Path}]"));
            File.WriteAllText(_healthPath, text, new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    private static void SafeDeleteRecord(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void SafeDeleteTransaction(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static bool TryValidateItemId(string? value, out string itemId)
    {
        itemId = string.Empty;
        if (!Guid.TryParseExact(value, "N", out Guid id)) return false;
        itemId = id.ToString("N");
        return true;
    }

    private static bool IsValidHash(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private string PayloadPath(string itemId) => Path.Combine(_storeRoot, itemId + ".sentinelq");
    private string TempPayloadPath(string itemId) => Path.Combine(_storeRoot, itemId + ".tmp");
    private string RecordPath(string itemId) => Path.Combine(_recordsRoot, itemId + ".json");
    private string TransactionPath(string itemId) => Path.Combine(_transactionRoot, itemId + ".json");

    private static string GetFinalPath(SafeFileHandle handle)
    {
        StringBuilder buffer = new(512);
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0) throw new IOException(Win32("GetFinalPathNameByHandle failed."));
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder((int)length + 1);
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0) throw new IOException(Win32("GetFinalPathNameByHandle failed."));
        }
        return NormalizeDevicePath(buffer.ToString());
    }

    private static string NormalizeDevicePath(string path)
    {
        if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
            return "\\\\" + path[8..];
        if (path.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase))
            return path[4..];
        return path;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
                .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void Hit(QuarantineCheckpoint checkpoint) => _checkpoint?.Invoke(checkpoint);
    private static QuarantineStoreResult Ok(string itemId, string sha256, string message) => new(true, "Success", message, itemId, sha256);
    private static QuarantineStoreResult Fail(string code, string message) => new(false, code, message, string.Empty, string.Empty);
    private static string Win32(string prefix) => $"{prefix} Win32={Marshal.GetLastWin32Error()}.";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle hFile,
        int fileInformationClass,
        ref FileDispositionInfo lpFileInformation,
        uint dwBufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle hFile,
        int fileInformationClass,
        IntPtr lpFileInformation,
        uint dwBufferSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        internal bool DeleteFile;
    }

    private readonly record struct StableFileIdentity(string FinalPath, uint VolumeSerial, ulong FileIndex, uint LinkCount);
    private readonly record struct RecordReadResult(RecordStatus Status, ProtectedQuarantineRecord? Record);
    private enum RecordStatus { Missing, Valid, Corrupt }

    internal sealed class DirectoryLease : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = new();
        internal bool Succeeded { get; private set; }
        internal string Error { get; private set; } = string.Empty;
        internal SafeFileHandle ParentHandle => _handles[0];

        private DirectoryLease() { }

        internal static DirectoryLease Acquire(string parent)
        {
            DirectoryLease lease = new();
            try
            {
                string current = Path.GetFullPath(parent);
                while (true)
                {
                    SafeFileHandle handle = CreateFileW(
                        current,
                        FileListDirectory | FileReadAttributes,
                        FileShareRead | FileShareWrite,
                        IntPtr.Zero,
                        OpenExisting,
                        FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                        IntPtr.Zero);
                    if (handle.IsInvalid)
                    {
                        handle.Dispose();
                        lease.Error = Win32("Sentinel could not lock the restore directory chain against rename/reparse substitution.");
                        lease.Dispose();
                        return lease;
                    }
                    if (!GetFileInformationByHandle(handle, out ByHandleFileInformation info) || (info.FileAttributes & FileAttributeReparsePoint) != 0)
                    {
                        handle.Dispose();
                        lease.Error = "The restore directory chain contains an unverifiable or reparse-point directory.";
                        lease.Dispose();
                        return lease;
                    }
                    string final = GetFinalPath(handle);
                    if (!PathsEqual(final, current))
                    {
                        handle.Dispose();
                        lease.Error = "A restore directory resolved to a different filesystem location.";
                        lease.Dispose();
                        return lease;
                    }
                    lease._handles.Add(handle);
                    DirectoryInfo? above = Directory.GetParent(current);
                    if (above is null) break;
                    current = above.FullName;
                }
                lease.Succeeded = lease._handles.Count > 0;
                return lease;
            }
            catch (Exception ex)
            {
                lease.Error = $"Sentinel could not secure the restore directory chain ({ex.GetType().Name}).";
                lease.Dispose();
                return lease;
            }
        }

        public void Dispose()
        {
            foreach (SafeFileHandle handle in _handles) handle.Dispose();
            _handles.Clear();
        }
    }
}

internal sealed record QuarantineStoreResult(bool Succeeded, string Code, string Message, string ItemId, string Sha256);
internal sealed record ProtectedQuarantineRecord(string ItemId, string OriginalPath, string Sha256, DateTimeOffset QuarantinedAtUtc);
internal sealed record QuarantineTransaction(string ItemId, string Operation, string Stage, string Path, string TempPath, string Sha256, DateTimeOffset CreatedAtUtc);
internal sealed record QuarantineStoreIssue(string Code, string Path, string Message);
internal sealed record QuarantineStoreSnapshot(IReadOnlyList<ProtectedQuarantineRecord> Records, IReadOnlyList<QuarantineStoreIssue> Issues);

internal enum QuarantineCheckpoint
{
    QuarantineIntentPersisted,
    QuarantineTemporaryCopyReady,
    QuarantinePayloadReady,
    QuarantineSourceDeleted,
    QuarantineRecordCommitted,
    RestoreIntentPersisted,
    RestoreTemporaryCopyReady,
    RestoreDestinationReady,
    RestorePayloadDeleted,
    DeleteIntentPersisted,
    DeletePayloadDeleted
}
