using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal enum RelatedArtifactClassification
{
    ConfirmedCopy = 0,
    LikelyAttributableCopy,
    MetadataReferenceOnly,
    UnverifiedCandidate
}

internal enum RelatedArtifactProviderState
{
    Completed = 0,
    Limited,
    Unavailable,
    Failed,
    Canceled
}

internal sealed record RelatedArtifactCandidate(
    string Provider,
    string Location,
    RelatedArtifactClassification Classification,
    string Evidence,
    bool TargetedRemovalSupported,
    bool IsSameFilesystemObject,
    bool RemoteStateVerified);

internal sealed record RelatedArtifactProviderResult(
    string Provider,
    RelatedArtifactProviderState State,
    string Source,
    IReadOnlyList<RelatedArtifactCandidate> Candidates,
    int FilesScanned,
    long BytesHashed,
    string Limitation);

internal sealed record RelatedArtifactDiscoveryResult(
    bool Succeeded,
    string SourceSha256,
    IReadOnlyList<RelatedArtifactProviderResult> Providers,
    IReadOnlyList<RelatedArtifactCandidate> Candidates,
    bool WasLimited,
    bool WasCanceled,
    string Summary);

internal sealed record RelatedArtifactDiscoveryBounds(
    int MaximumFiles,
    long MaximumBytesHashed,
    int MaximumDirectoryDepth,
    TimeSpan MaximumDuration,
    int MaximumConcurrentHashers)
{
    internal static RelatedArtifactDiscoveryBounds Default { get; } = new(
        MaximumFiles: 25_000,
        MaximumBytesHashed: 16L * 1024 * 1024 * 1024,
        MaximumDirectoryDepth: 12,
        MaximumDuration: TimeSpan.FromMinutes(5),
        MaximumConcurrentHashers: 2);

    internal void Validate()
    {
        if (MaximumFiles is < 1 or > 1_000_000 ||
            MaximumBytesHashed is < 1 or > 4L * 1024 * 1024 * 1024 * 1024 ||
            MaximumDirectoryDepth is < 0 or > 64 ||
            MaximumDuration <= TimeSpan.Zero || MaximumDuration > TimeSpan.FromHours(4) ||
            MaximumConcurrentHashers is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(RelatedArtifactDiscoveryBounds));
    }
}

internal sealed record SentinelArtifactProvenance(
    string Path,
    string SourceSha256,
    string OperationId,
    string ArtifactKind);

internal sealed record RelatedArtifactMetadataReference(
    string Provider,
    string Reference,
    string Evidence,
    bool TargetedRemovalSupported,
    bool ReliableAttribution,
    bool RemoteStateVerified = false);

internal sealed record RelatedArtifactDiscoveryRequest(
    string SourcePath,
    IReadOnlyList<string> HashSearchRoots,
    IReadOnlyList<SentinelArtifactProvenance>? SentinelArtifacts = null,
    IReadOnlyList<RelatedArtifactMetadataReference>? MetadataReferences = null,
    IReadOnlyList<string>? FileHistoryRoots = null,
    IReadOnlyList<string>? PreviousVersionRoots = null,
    bool IncludeLocalOneDriveRoots = false,
    RelatedArtifactDiscoveryBounds? Bounds = null);

/// <summary>
/// Bounded, cancellation-aware related-artifact discovery. Discovery never grants deletion
/// authority. Exact hash/provenance may classify a candidate, but every filesystem cleanup
/// must independently validate that candidate as a new exact target.
/// </summary>
internal sealed class RelatedArtifactDiscoveryService
{
    private const int HashBufferSize = 128 * 1024;

    internal async Task<RelatedArtifactDiscoveryResult> DiscoverAsync(
        RelatedArtifactDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RelatedArtifactDiscoveryBounds bounds = request.Bounds ?? RelatedArtifactDiscoveryBounds.Default;
        bounds.Validate();

        if (!TryNormalizeExistingRegularFile(request.SourcePath, out string source, out string sourceError))
            return Failure(sourceError);

        Stopwatch stopwatch = Stopwatch.StartNew();
        string sourceHash;
        long sourceBytes;
        try
        {
            (sourceHash, sourceBytes) = await HashFileAsync(source, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(false, string.Empty, Array.Empty<RelatedArtifactProviderResult>(), Array.Empty<RelatedArtifactCandidate>(), false, true,
                "Discovery was canceled before source hashing completed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return Failure("The selected source could not be hashed safely.");
        }

        FileObjectIdentity? sourceIdentity = TryReadObjectIdentity(source);
        DiscoveryBudget budget = new(bounds, stopwatch, sourceBytes);
        List<RelatedArtifactProviderResult> providers = new();

        providers.Add(await DiscoverSentinelArtifactsAsync(
            source, sourceHash, sourceIdentity, request.SentinelArtifacts, budget, cancellationToken).ConfigureAwait(false));

        providers.Add(await DiscoverFilesystemCopiesAsync(
            "BoundedHashDuplicateSearch", source, sourceHash, sourceIdentity,
            request.HashSearchRoots, budget, cancellationToken).ConfigureAwait(false));

        providers.Add(await DiscoverFilesystemCopiesAsync(
            "FileHistory", source, sourceHash, sourceIdentity,
            request.FileHistoryRoots ?? Array.Empty<string>(), budget, cancellationToken,
            noRootsMeansUnavailable: true,
            unavailableReason: "No safely inspectable File History backup root was supplied/discovered; no broad history deletion was attempted.").ConfigureAwait(false));

        providers.Add(await DiscoverFilesystemCopiesAsync(
            "PreviousVersions", source, sourceHash, sourceIdentity,
            request.PreviousVersionRoots ?? Array.Empty<string>(), budget, cancellationToken,
            noRootsMeansUnavailable: true,
            unavailableReason: "No safely mounted/read-only Previous Versions snapshot root was supplied/discovered; Sentinel did not destroy a shadow set.").ConfigureAwait(false));

        providers.Add(DiscoverMetadataReferences(request.MetadataReferences, "WindowsSearch"));
        providers.Add(DiscoverMetadataReferences(request.MetadataReferences, "RecentJumpLists"));

        providers.Add(request.IncludeLocalOneDriveRoots
            ? await DiscoverFilesystemCopiesAsync(
                "OneDriveLocal", source, sourceHash, sourceIdentity,
                GetOneDriveRoots(), budget, cancellationToken,
                noRootsMeansUnavailable: true,
                unavailableReason: "No local OneDrive synchronization root was available. Remote provider state was not queried.").ConfigureAwait(false)
            : new RelatedArtifactProviderResult(
                "OneDriveLocal", RelatedArtifactProviderState.Unavailable, "local sync roots",
                Array.Empty<RelatedArtifactCandidate>(), 0, 0,
                "Local OneDrive discovery was not requested. Remote history/state remains unverified."));

        List<RelatedArtifactCandidate> all = providers.SelectMany(p => p.Candidates).ToList();
        bool canceled = providers.Any(p => p.State == RelatedArtifactProviderState.Canceled);
        bool limited = providers.Any(p => p.State is RelatedArtifactProviderState.Limited or RelatedArtifactProviderState.Unavailable or RelatedArtifactProviderState.Failed);
        return new(!canceled, sourceHash, providers, all, limited, canceled, BuildSummary(all, providers));
    }

    private static RelatedArtifactDiscoveryResult Failure(string message) =>
        new(false, string.Empty, Array.Empty<RelatedArtifactProviderResult>(), Array.Empty<RelatedArtifactCandidate>(), false, false, message);

    private static async Task<RelatedArtifactProviderResult> DiscoverSentinelArtifactsAsync(
        string source,
        string sourceHash,
        FileObjectIdentity? sourceIdentity,
        IReadOnlyList<SentinelArtifactProvenance>? artifacts,
        DiscoveryBudget budget,
        CancellationToken cancellationToken)
    {
        const string provider = "SentinelArtifacts";
        const string providerSource = "registered Sentinel provenance";
        if (artifacts is null || artifacts.Count == 0)
            return new(provider, RelatedArtifactProviderState.Completed, providerSource, Array.Empty<RelatedArtifactCandidate>(), 0, 0, string.Empty);

        List<RelatedArtifactCandidate> candidates = new();
        int files = 0;
        long bytes = 0;
        foreach (SentinelArtifactProvenance artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!budget.CanContinue())
                return Limited(provider, providerSource, candidates, files, bytes, budget);

            if (!IsValidSha256Hex(artifact.SourceSha256) ||
                !artifact.SourceSha256.Equals(sourceHash, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(artifact.OperationId) || string.IsNullOrWhiteSpace(artifact.Path))
                continue;

            if (!TryNormalizeExistingRegularFile(artifact.Path, out string candidatePath, out _) ||
                candidatePath.Equals(source, StringComparison.OrdinalIgnoreCase))
                continue;

            long length;
            try { length = new FileInfo(candidatePath).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            if (!budget.TryReserveFile())
                return Limited(provider, providerSource, candidates, files, bytes, budget);
            files++;

            // Sentinel provenance is strong attribution evidence, but content still must be
            // hashed for confirmation. Reserve the byte budget before any read so this provider
            // cannot bypass the same global bound enforced for directory/history providers.
            if (!budget.TryReserveBytes(length))
                return Limited(provider, providerSource, candidates, files, bytes, budget);

            string candidateHash;
            try
            {
                (candidateHash, _) = await HashFileAsync(candidatePath, cancellationToken).ConfigureAwait(false);
                bytes += length;
            }
            catch (OperationCanceledException)
            {
                return new(provider, RelatedArtifactProviderState.Canceled, providerSource, candidates, files, bytes, "Canceled.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                continue;
            }

            FileObjectIdentity? identity = TryReadObjectIdentity(candidatePath);
            bool sameObject = sourceIdentity.HasValue && identity.HasValue && sourceIdentity.Value == identity.Value;
            if (!candidateHash.Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                continue;

            candidates.Add(new(
                provider,
                candidatePath,
                sameObject ? RelatedArtifactClassification.UnverifiedCandidate : RelatedArtifactClassification.ConfirmedCopy,
                sameObject
                    ? "Sentinel provenance and content match, but this path resolves to the same filesystem object/hardlink and is not an independent deletion target."
                    : "Stable Sentinel provenance plus exact SHA-256 content match (operation " + artifact.OperationId + ", kind " + artifact.ArtifactKind + ").",
                TargetedRemovalSupported: !sameObject,
                IsSameFilesystemObject: sameObject,
                RemoteStateVerified: false));
        }

        return new(provider, RelatedArtifactProviderState.Completed, providerSource, candidates, files, bytes, string.Empty);
    }

    private static async Task<RelatedArtifactProviderResult> DiscoverFilesystemCopiesAsync(
        string provider,
        string source,
        string sourceHash,
        FileObjectIdentity? sourceIdentity,
        IReadOnlyList<string>? roots,
        DiscoveryBudget budget,
        CancellationToken cancellationToken,
        bool noRootsMeansUnavailable = false,
        string unavailableReason = "")
    {
        string[] normalizedRoots = NormalizeRoots(roots);
        if (normalizedRoots.Length == 0)
        {
            return new(provider,
                noRootsMeansUnavailable ? RelatedArtifactProviderState.Unavailable : RelatedArtifactProviderState.Completed,
                string.Join(";", roots ?? Array.Empty<string>()),
                Array.Empty<RelatedArtifactCandidate>(), 0, 0,
                noRootsMeansUnavailable ? unavailableReason : string.Empty);
        }

        string providerSource = string.Join(";", normalizedRoots);
        List<RelatedArtifactCandidate> candidates = new();
        int files = 0;
        long bytes = 0;
        long sourceLength;
        try { sourceLength = new FileInfo(source).Length; }
        catch { return new(provider, RelatedArtifactProviderState.Failed, providerSource, candidates, 0, 0, "The selected source changed or became inaccessible during discovery."); }

        foreach (string root in normalizedRoots)
        {
            Stack<(string Path, int Depth)> pending = new();
            pending.Push((root, 0));
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!budget.CanContinue()) return Limited(provider, providerSource, candidates, files, bytes, budget);

                (string directory, int depth) = pending.Pop();
                IEnumerable<string> entries;
                try { entries = Directory.EnumerateFileSystemEntries(directory); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                foreach (string entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!budget.CanContinue()) return Limited(provider, providerSource, candidates, files, bytes, budget);

                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(entry); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (depth < budget.Bounds.MaximumDirectoryDepth) pending.Push((entry, depth + 1));
                        continue;
                    }

                    string candidatePath;
                    try { candidatePath = Path.GetFullPath(entry); }
                    catch { continue; }
                    if (candidatePath.Equals(source, StringComparison.OrdinalIgnoreCase)) continue;

                    long length;
                    try { length = new FileInfo(candidatePath).Length; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                    if (!budget.TryReserveFile()) return Limited(provider, providerSource, candidates, files, bytes, budget);
                    files++;

                    // Size only avoids unnecessary hashing; size itself never proves attribution.
                    if (length != sourceLength) continue;
                    if (!budget.TryReserveBytes(length)) return Limited(provider, providerSource, candidates, files, bytes, budget);

                    string candidateHash;
                    try
                    {
                        (candidateHash, _) = await HashFileAsync(candidatePath, cancellationToken).ConfigureAwait(false);
                        bytes += length;
                    }
                    catch (OperationCanceledException)
                    {
                        return new(provider, RelatedArtifactProviderState.Canceled, providerSource, candidates, files, bytes, "Canceled.");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
                    {
                        continue;
                    }

                    if (!candidateHash.Equals(sourceHash, StringComparison.OrdinalIgnoreCase)) continue;
                    FileObjectIdentity? identity = TryReadObjectIdentity(candidatePath);
                    bool sameObject = sourceIdentity.HasValue && identity.HasValue && sourceIdentity.Value == identity.Value;
                    candidates.Add(new(
                        provider,
                        candidatePath,
                        sameObject ? RelatedArtifactClassification.UnverifiedCandidate : RelatedArtifactClassification.ConfirmedCopy,
                        sameObject
                            ? "Exact SHA-256 match resolves to the same filesystem object/hardlink; no independent deletion authority is created."
                            : "Exact SHA-256 content match after bounded scan.",
                        TargetedRemovalSupported: !sameObject,
                        IsSameFilesystemObject: sameObject,
                        RemoteStateVerified: false));
                }
            }
        }

        return new(provider, RelatedArtifactProviderState.Completed, providerSource, candidates, files, bytes, string.Empty);
    }

    private static RelatedArtifactProviderResult Limited(
        string provider,
        string source,
        IReadOnlyList<RelatedArtifactCandidate> candidates,
        int files,
        long bytes,
        DiscoveryBudget budget) =>
        new(provider, RelatedArtifactProviderState.Limited, source, candidates, files, bytes, budget.LimitReason);

    private static RelatedArtifactProviderResult DiscoverMetadataReferences(
        IReadOnlyList<RelatedArtifactMetadataReference>? references,
        string provider)
    {
        RelatedArtifactMetadataReference[] matches = (references ?? Array.Empty<RelatedArtifactMetadataReference>())
            .Where(r => string.Equals(r.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
            return new(provider, RelatedArtifactProviderState.Unavailable, "provider metadata", Array.Empty<RelatedArtifactCandidate>(), 0, 0,
                provider + " provider metadata was not available to this discovery session.");

        RelatedArtifactCandidate[] candidates = matches.Select(r => new RelatedArtifactCandidate(
            provider,
            r.Reference,
            r.ReliableAttribution ? RelatedArtifactClassification.MetadataReferenceOnly : RelatedArtifactClassification.UnverifiedCandidate,
            r.Evidence,
            r.ReliableAttribution && r.TargetedRemovalSupported,
            IsSameFilesystemObject: false,
            r.RemoteStateVerified)).ToArray();
        return new(provider, RelatedArtifactProviderState.Completed, "provider metadata", candidates, 0, 0, string.Empty);
    }

    private static string[] GetOneDriveRoots()
    {
        string[] names = { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" };
        return names.Select(Environment.GetEnvironmentVariable)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] NormalizeRoots(IReadOnlyList<string>? roots)
    {
        if (roots is null) return Array.Empty<string>();
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? raw in roots)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            try
            {
                if (!Path.IsPathFullyQualified(raw)) continue;
                string path = Path.GetFullPath(raw);
                if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                result.Add(path);
            }
            catch { }
        }
        return result.ToArray();
    }

    private static bool TryNormalizeExistingRegularFile(string? path, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                error = "A fully-qualified selected file is required.";
                return false;
            }
            fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) || (File.GetAttributes(fullPath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                error = "The selected source is missing, inaccessible, a directory, or a reparse point.";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            error = "The selected source could not be validated.";
            return false;
        }
    }

    private static async Task<(string Hash, long Bytes)> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, HashBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[HashBufferSize];
        long bytes = 0;
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                bytes += read;
            }
            return (Convert.ToHexString(hash.GetHashAndReset()), bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static bool IsValidSha256Hex(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string BuildSummary(
        IReadOnlyList<RelatedArtifactCandidate> candidates,
        IReadOnlyList<RelatedArtifactProviderResult> providers)
    {
        int confirmed = candidates.Count(c => c.Classification == RelatedArtifactClassification.ConfirmedCopy);
        int likely = candidates.Count(c => c.Classification == RelatedArtifactClassification.LikelyAttributableCopy);
        int metadata = candidates.Count(c => c.Classification == RelatedArtifactClassification.MetadataReferenceOnly);
        int unverified = candidates.Count(c => c.Classification == RelatedArtifactClassification.UnverifiedCandidate);
        int unavailable = providers.Count(p => p.State is RelatedArtifactProviderState.Unavailable or RelatedArtifactProviderState.Failed or RelatedArtifactProviderState.Limited);
        return $"Discovery found {confirmed} confirmed, {likely} likely, {metadata} metadata-reference, and {unverified} unverified candidates. {unavailable} provider(s) were unavailable, failed, or limited.";
    }

    private static FileObjectIdentity? TryReadObjectIdentity(string path)
    {
        const uint FileReadAttributes = 0x80;
        const uint ShareRead = 0x1;
        const uint ShareWrite = 0x2;
        const uint ShareDelete = 0x4;
        const uint OpenExisting = 3;
        const uint OpenReparsePoint = 0x00200000;
        const int FileIdInfoClass = 18;

        using SafeFileHandle handle = CreateFileW(path, FileReadAttributes, ShareRead | ShareWrite | ShareDelete,
            IntPtr.Zero, OpenExisting, OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        int size = Marshal.SizeOf<FILE_ID_INFO>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetFileInformationByHandleEx(handle, FileIdInfoClass, buffer, (uint)size)) return null;
            FILE_ID_INFO info = Marshal.PtrToStructure<FILE_ID_INFO>(buffer);
            return new FileObjectIdentity(info.VolumeSerialNumber, info.FileId.LowPart, info.FileId.HighPart);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private sealed class DiscoveryBudget
    {
        private int _files;
        private long _bytes;
        private string _limitReason = string.Empty;
        private readonly Stopwatch _stopwatch;

        internal DiscoveryBudget(RelatedArtifactDiscoveryBounds bounds, Stopwatch stopwatch, long initialBytes)
        {
            Bounds = bounds;
            _stopwatch = stopwatch;
            _bytes = initialBytes;
        }

        internal RelatedArtifactDiscoveryBounds Bounds { get; }
        internal string LimitReason => string.IsNullOrEmpty(_limitReason) ? "A configured discovery bound was reached." : _limitReason;

        internal bool CanContinue()
        {
            if (_stopwatch.Elapsed > Bounds.MaximumDuration)
            {
                _limitReason = "The discovery duration bound was reached.";
                return false;
            }
            if (_files >= Bounds.MaximumFiles)
            {
                _limitReason = "The file-count discovery bound was reached.";
                return false;
            }
            if (_bytes >= Bounds.MaximumBytesHashed)
            {
                _limitReason = "The hashed-byte discovery bound was reached.";
                return false;
            }
            return true;
        }

        internal bool TryReserveFile()
        {
            if (!CanContinue()) return false;
            if (_files + 1 > Bounds.MaximumFiles)
            {
                _limitReason = "The file-count discovery bound was reached.";
                return false;
            }
            _files++;
            return true;
        }

        internal bool TryReserveBytes(long length)
        {
            if (length < 0 || _bytes > Bounds.MaximumBytesHashed - length)
            {
                _limitReason = "The hashed-byte discovery bound was reached.";
                return false;
            }
            _bytes += length;
            return true;
        }
    }

    private readonly record struct FileObjectIdentity(ulong VolumeSerialNumber, ulong FileIdLow, ulong FileIdHigh);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ID_128 { public ulong LowPart; public ulong HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ID_INFO { public ulong VolumeSerialNumber; public FILE_ID_128 FileId; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file, int fileInformationClass, IntPtr fileInformation, uint bufferSize);
}
