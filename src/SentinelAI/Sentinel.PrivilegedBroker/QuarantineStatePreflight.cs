internal static class QuarantineStatePreflight
{
    internal static IReadOnlyList<QuarantineStoreIssue> Validate(string root)
    {
        List<QuarantineStoreIssue> issues = new();
        issues.AddRange(QuarantineRecoveryGuard.Validate(root));

        string recordsRoot = Path.Combine(Path.GetFullPath(root), "QuarantineRecords");
        if (!Directory.Exists(recordsRoot))
            return issues;

        foreach (string recordPath in Directory.EnumerateFiles(recordsRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            string fileId = Path.GetFileNameWithoutExtension(recordPath);
            if (!Guid.TryParseExact(fileId, "N", out Guid parsedId))
                continue;

            string normalizedId = parsedId.ToString("N");
            if (!BoundedProtectedJsonFile.TryRead<ProtectedQuarantineRecord>(recordPath, out ProtectedQuarantineRecord? record) ||
                record is null ||
                !record.ItemId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase) ||
                !IsValidHash(record.Sha256) ||
                !TryValidateOriginalPath(record.OriginalPath))
            {
                issues.Add(new(
                    "CorruptRecord",
                    recordPath,
                    "The protected quarantine record was oversized, unreadable, malformed, or structurally invalid. Privileged quarantine operations remain blocked and the evidence was preserved."));
            }
        }

        return issues;
    }

    private static bool IsValidHash(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool TryValidateOriginalPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            string full = Path.GetFullPath(raw);
            return Path.IsPathFullyQualified(full);
        }
        catch
        {
            return false;
        }
    }
}
