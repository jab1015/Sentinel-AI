using System.Text.Json;

internal static class QuarantineRecoveryGuard
{
    internal static IReadOnlyList<QuarantineStoreIssue> Validate(string root)
    {
        string fullRoot = Path.GetFullPath(root);
        string storeRoot = Path.Combine(fullRoot, "QuarantineStore");
        string recordsRoot = Path.Combine(fullRoot, "QuarantineRecords");
        string transactionRoot = Path.Combine(fullRoot, "Transactions");
        List<QuarantineStoreIssue> issues = new();

        if (!Directory.Exists(transactionRoot)) return issues;

        foreach (string transactionPath in Directory.EnumerateFiles(transactionRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            QuarantineTransaction? txn;
            try
            {
                txn = JsonSerializer.Deserialize<QuarantineTransaction>(File.ReadAllText(transactionPath));
            }
            catch
            {
                issues.Add(new("CorruptTransaction", transactionPath,
                    "Automatic quarantine recovery was blocked because a transaction could not be parsed. Evidence was preserved."));
                continue;
            }

            if (txn is null || !TryNormalizeId(txn.ItemId, out string itemId) ||
                !Path.GetFileNameWithoutExtension(transactionPath).Equals(itemId, StringComparison.OrdinalIgnoreCase) ||
                !IsValidHash(txn.Sha256))
            {
                issues.Add(new("CorruptTransaction", transactionPath,
                    "Automatic quarantine recovery was blocked because transaction identity or hash metadata was invalid. Evidence was preserved."));
                continue;
            }

            switch (txn.Operation)
            {
                case "Quarantine":
                    ValidateQuarantine(txn, transactionPath, itemId, storeRoot, issues);
                    break;
                case "Restore":
                    ValidateRecordBoundTransaction(txn, transactionPath, itemId, recordsRoot, isRestore: true, issues);
                    break;
                case "Delete":
                    ValidateRecordBoundTransaction(txn, transactionPath, itemId, recordsRoot, isRestore: false, issues);
                    break;
                default:
                    issues.Add(new("UnknownTransaction", transactionPath,
                        "Automatic quarantine recovery was blocked because the transaction operation is not allowlisted. Evidence was preserved."));
                    break;
            }
        }

        return issues;
    }

    private static void ValidateQuarantine(
        QuarantineTransaction txn,
        string transactionPath,
        string itemId,
        string storeRoot,
        List<QuarantineStoreIssue> issues)
    {
        if (txn.Stage is not ("Prepared" or "PayloadReady"))
        {
            issues.Add(Mismatch(transactionPath, "The quarantine transaction stage is not recognized."));
            return;
        }

        if (!TryFullPath(txn.Path, out string source) || !Path.IsPathFullyQualified(source))
        {
            issues.Add(Mismatch(transactionPath, "The quarantine source path is invalid."));
            return;
        }

        string expectedTemp = Path.Combine(storeRoot, itemId + ".tmp");
        if (!TryFullPath(txn.TempPath, out string temp) || !PathsEqual(temp, expectedTemp))
            issues.Add(Mismatch(transactionPath, "The quarantine temporary payload path is not bound to the protected store."));
    }

    private static void ValidateRecordBoundTransaction(
        QuarantineTransaction txn,
        string transactionPath,
        string itemId,
        string recordsRoot,
        bool isRestore,
        List<QuarantineStoreIssue> issues)
    {
        string recordPath = Path.Combine(recordsRoot, itemId + ".json");
        ProtectedQuarantineRecord? record;
        try
        {
            if (!File.Exists(recordPath))
            {
                issues.Add(Mismatch(transactionPath, "The transaction has no protected record to bind recovery to."));
                return;
            }
            record = JsonSerializer.Deserialize<ProtectedQuarantineRecord>(File.ReadAllText(recordPath));
        }
        catch
        {
            issues.Add(Mismatch(transactionPath, "The protected record could not be parsed for recovery binding."));
            return;
        }

        if (record is null || !record.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase) ||
            !IsValidHash(record.Sha256) ||
            !record.Sha256.Equals(txn.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !TryFullPath(record.OriginalPath, out string recordPathFull) ||
            !TryFullPath(txn.Path, out string transactionPathFull) ||
            !PathsEqual(recordPathFull, transactionPathFull))
        {
            issues.Add(Mismatch(transactionPath, "The transaction path/hash is not exactly bound to its protected record."));
            return;
        }

        if (!isRestore)
        {
            if (txn.Stage != "Prepared" || !string.IsNullOrEmpty(txn.TempPath))
                issues.Add(Mismatch(transactionPath, "The delete transaction contains an invalid stage or temporary path."));
            return;
        }

        if (txn.Stage is not ("Prepared" or "TempReady" or "DestinationReady"))
        {
            issues.Add(Mismatch(transactionPath, "The restore transaction stage is not recognized."));
            return;
        }

        string? parent = Path.GetDirectoryName(recordPathFull);
        if (string.IsNullOrWhiteSpace(parent) || !TryFullPath(txn.TempPath, out string tempPath) ||
            !PathsEqual(Path.GetDirectoryName(tempPath) ?? string.Empty, parent))
        {
            issues.Add(Mismatch(transactionPath, "The restore temporary path is outside the record-bound destination directory."));
            return;
        }

        string expectedPrefix = "." + Path.GetFileName(recordPathFull) + ".sentinel-restore-" + itemId + "-";
        string tempName = Path.GetFileName(tempPath);
        if (!tempName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) ||
            !tempName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Mismatch(transactionPath, "The restore temporary filename does not match Sentinel's bounded recovery pattern."));
        }
    }

    private static QuarantineStoreIssue Mismatch(string transactionPath, string detail) =>
        new("TransactionRecordMismatch", transactionPath,
            detail + " Automatic recovery was blocked and all evidence was preserved.");

    private static bool TryNormalizeId(string? raw, out string id)
    {
        id = string.Empty;
        if (!Guid.TryParseExact(raw, "N", out Guid parsed)) return false;
        id = parsed.ToString("N");
        return true;
    }

    private static bool IsValidHash(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool TryFullPath(string? raw, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            path = Path.GetFullPath(raw);
            return Path.IsPathFullyQualified(path);
        }
        catch { return false; }
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
}
