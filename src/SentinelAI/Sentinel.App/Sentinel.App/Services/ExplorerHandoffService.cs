using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sentinel.App.Services;

internal enum ExplorerRequestedAction
{
    Inspect = 0,
    EncryptFile,
    AddToVault,
    SecureDelete
}

internal sealed class ExplorerHandoffService
{
    internal const int ProtocolVersion = 1;
    internal const int MaximumItems = 16;
    internal const int MaximumRecordBytes = 64 * 1024;
    internal const int MaximumPathCharacters = 32_767;
    internal const string HandoffArgument = "--sentinel-explorer-handoff";
    internal const string InspectArgument = HandoffArgument;
    internal const string InspectCommand = "inspect";
    internal const string EncryptCommand = "encrypt";
    internal const string VaultCommand = "vault";
    internal const string SecureDeleteCommand = "secure-delete";

    private static readonly TimeSpan MaximumRecordAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string _handoffRoot;

    internal ExplorerHandoffService(string handoffRoot)
    {
        if (string.IsNullOrWhiteSpace(handoffRoot))
            throw new ArgumentException("A handoff root is required.", nameof(handoffRoot));

        _handoffRoot = Path.GetFullPath(handoffRoot);
    }

    internal static bool TryParseHandoffId(IReadOnlyList<string>? arguments, out Guid handoffId)
    {
        handoffId = Guid.Empty;
        if (arguments is null || arguments.Count == 0) return false;

        for (int i = 0; i < arguments.Count; i++)
        {
            if (!string.Equals(arguments[i], HandoffArgument, StringComparison.Ordinal))
                continue;

            if (i + 1 >= arguments.Count || i + 2 < arguments.Count)
                return false;

            return Guid.TryParseExact(arguments[i + 1], "N", out handoffId) && handoffId != Guid.Empty;
        }

        return false;
    }

    internal static bool TryParseHandoffId(string? launchArguments, out Guid handoffId)
    {
        handoffId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(launchArguments)) return false;

        List<string> tokens = Tokenize(launchArguments);
        return TryParseHandoffId(tokens, out handoffId);
    }

    /// <summary>
    /// Consumes an untrusted Explorer request. The command and paths are activation intent
    /// only. They never carry entitlement, cryptographic, broker, or deletion authority.
    /// Destructive/cryptographic actions require fresh in-app confirmation, authoritative
    /// premium entitlement, and their own exact-object safety validation.
    /// </summary>
    internal bool TryConsume(Guid handoffId, out ExplorerInspectionRequest? request, out string reason)
    {
        request = null;
        reason = string.Empty;
        if (handoffId == Guid.Empty)
        {
            reason = "The Explorer handoff identifier was invalid.";
            return false;
        }

        string path = Path.Combine(_handoffRoot, handoffId.ToString("N") + ".json");
        string expectedFullPath = Path.GetFullPath(path);
        string rootPrefix = _handoffRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!expectedFullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            reason = "The Explorer handoff path escaped its allowed directory.";
            return false;
        }

        try
        {
            using FileStream stream = new(expectedFullPath, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumRecordBytes)
            {
                reason = "The Explorer handoff record was empty or oversized.";
                return false;
            }

            ExplorerHandoffRecord? record = JsonSerializer.Deserialize<ExplorerHandoffRecord>(stream, StrictJson);
            if (!TryValidateRecord(record, out ExplorerRequestedAction action, out IReadOnlyList<string>? paths, out reason))
                return false;

            request = new ExplorerInspectionRequest(handoffId, action, paths!);
            return true;
        }
        catch (FileNotFoundException)
        {
            reason = "The Explorer handoff record no longer exists.";
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            reason = "The Explorer handoff directory is unavailable.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            reason = "Windows denied access to the Explorer handoff record.";
            return false;
        }
        catch (JsonException)
        {
            reason = "The Explorer handoff record was malformed.";
            return false;
        }
        catch (IOException)
        {
            reason = "The Explorer handoff record could not be read exclusively.";
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "The Explorer handoff record contained an invalid path.";
            return false;
        }
        finally
        {
            try { File.Delete(expectedFullPath); } catch { }
        }
    }

    private static bool TryValidateRecord(
        ExplorerHandoffRecord? record,
        out ExplorerRequestedAction action,
        out IReadOnlyList<string>? paths,
        out string reason)
    {
        action = default;
        paths = null;
        reason = string.Empty;
        if (record is null || record.Version != ProtocolVersion || !TryMapCommand(record.Command, out action))
        {
            reason = "The Explorer handoff record used an unsupported version or command.";
            return false;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset created;
        try { created = DateTimeOffset.FromUnixTimeMilliseconds(record.CreatedUnixMs); }
        catch (ArgumentOutOfRangeException)
        {
            reason = "The Explorer handoff timestamp was invalid.";
            return false;
        }

        if (created < now - MaximumRecordAge || created > now + MaximumFutureSkew)
        {
            reason = "The Explorer handoff record was stale or had an invalid future timestamp.";
            return false;
        }

        if (record.Items is null || record.Items.Count is < 1 or > MaximumItems)
        {
            reason = "The Explorer handoff record contained an unsupported item count.";
            return false;
        }

        if (action != ExplorerRequestedAction.Inspect && record.Items.Count != 1)
        {
            reason = "Premium Privacy Explorer actions currently require exactly one selected file.";
            return false;
        }

        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        List<string> normalized = new(record.Items.Count);
        foreach (string? raw in record.Items)
        {
            if (!TryNormalizeSelectedPath(raw, out string fullPath))
            {
                reason = "The Explorer selection contained an unsupported or unavailable filesystem item.";
                return false;
            }

            if (action != ExplorerRequestedAction.Inspect && Directory.Exists(fullPath))
            {
                reason = "Premium Privacy Explorer actions currently accept files only, not directories.";
                return false;
            }

            if (unique.Add(fullPath)) normalized.Add(fullPath);
        }

        if (normalized.Count == 0)
        {
            reason = "The Explorer selection did not contain a usable filesystem item.";
            return false;
        }

        paths = normalized;
        return true;
    }

    private static bool TryMapCommand(string? command, out ExplorerRequestedAction action)
    {
        if (string.Equals(command, InspectCommand, StringComparison.Ordinal))
        {
            action = ExplorerRequestedAction.Inspect;
            return true;
        }
        if (string.Equals(command, EncryptCommand, StringComparison.Ordinal))
        {
            action = ExplorerRequestedAction.EncryptFile;
            return true;
        }
        if (string.Equals(command, VaultCommand, StringComparison.Ordinal))
        {
            action = ExplorerRequestedAction.AddToVault;
            return true;
        }
        if (string.Equals(command, SecureDeleteCommand, StringComparison.Ordinal))
        {
            action = ExplorerRequestedAction.SecureDelete;
            return true;
        }

        action = default;
        return false;
    }

    private static bool TryNormalizeSelectedPath(string? raw, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaximumPathCharacters)
            return false;
        if (raw.IndexOfAny(new[] { '*', '?' }) >= 0)
            return false;

        string candidate = raw.Trim();
        if (candidate.StartsWith("\\\\.\\", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("\\\\?\\GLOBALROOT\\", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("\\\\?\\Device\\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(candidate)) return false;
            fullPath = Path.GetFullPath(candidate);
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static List<string> Tokenize(string value)
    {
        List<string> tokens = new();
        int index = 0;
        while (index < value.Length)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
            if (index >= value.Length) break;

            bool quoted = value[index] == '"';
            if (quoted) index++;
            int start = index;
            while (index < value.Length && (quoted ? value[index] != '"' : !char.IsWhiteSpace(value[index]))) index++;
            if (index > start) tokens.Add(value[start..index]);
            if (quoted && index < value.Length && value[index] == '"') index++;
        }
        return tokens;
    }

    private sealed record ExplorerHandoffRecord(
        int Version,
        string Command,
        long CreatedUnixMs,
        List<string?> Items);
}

internal sealed record ExplorerInspectionRequest(
    Guid HandoffId,
    ExplorerRequestedAction Action,
    IReadOnlyList<string> Paths)
{
    internal ExplorerInspectionRequest(Guid handoffId, IReadOnlyList<string> paths)
        : this(handoffId, ExplorerRequestedAction.Inspect, paths) { }
}
