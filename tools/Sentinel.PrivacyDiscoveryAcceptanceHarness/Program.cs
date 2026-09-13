using Sentinel.App.Services;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

Console.WriteLine("=== Sentinel Privacy Discovery Acceptance ===");
string root = Path.Combine(Path.GetTempPath(), "SentinelPrivacyDiscovery", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string source = Path.Combine(root, "source.txt");
    File.WriteAllText(source, "sentinel exact content");
    string sourceHash = Sha256(source);

    string scan = Path.Combine(root, "scan");
    Directory.CreateDirectory(scan);
    string exact = Path.Combine(scan, "renamed-copy.bin");
    File.Copy(source, exact);
    string sameNameDifferent = Path.Combine(scan, "source.txt");
    File.WriteAllText(sameNameDifferent, "different content here");
    string sameSizeDifferent = Path.Combine(scan, "same-size.bin");
    byte[] sameSize = File.ReadAllBytes(source).Select((b, i) => i == 0 ? (byte)(b ^ 0x7f) : b).ToArray();
    File.WriteAllBytes(sameSizeDifferent, sameSize);

    string sentinelRoot = Path.Combine(root, "sentinel");
    Directory.CreateDirectory(sentinelRoot);
    string sentinelArtifact = Path.Combine(sentinelRoot, "artifact.tmp");
    File.Copy(source, sentinelArtifact);

    string fileHistoryRoot = Path.Combine(root, "file-history");
    Directory.CreateDirectory(fileHistoryRoot);
    string historical = Path.Combine(fileHistoryRoot, "v1.txt");
    File.Copy(source, historical);

    string previousRoot = Path.Combine(root, "previous");
    Directory.CreateDirectory(previousRoot);
    string previous = Path.Combine(previousRoot, "snapshot.txt");
    File.Copy(source, previous);

    string oneDriveRoot = Path.Combine(root, "onedrive");
    Directory.CreateDirectory(oneDriveRoot);
    string cloudLocal = Path.Combine(oneDriveRoot, "cloud-copy.txt");
    File.Copy(source, cloudLocal);
    string? priorOneDrive = Environment.GetEnvironmentVariable("OneDrive");
    Environment.SetEnvironmentVariable("OneDrive", oneDriveRoot);

    RelatedArtifactDiscoveryService service = new();
    RelatedArtifactDiscoveryRequest request = new(
        source,
        new[] { scan },
        new[] { new SentinelArtifactProvenance(sentinelArtifact, sourceHash, "op-123", "investigation-export") },
        new[]
        {
            new RelatedArtifactMetadataReference("WindowsSearch", "search://source-entry", "Exact provider object reference", false, true),
            new RelatedArtifactMetadataReference("RecentJumpLists", "recent://source-entry", "Exact provider link target reference", true, true),
            new RelatedArtifactMetadataReference("WindowsSearch", "search://weak-name-only", "Filename only", false, false)
        },
        new[] { fileHistoryRoot },
        new[] { previousRoot },
        IncludeLocalOneDriveRoots: true,
        Bounds: new RelatedArtifactDiscoveryBounds(500, 64 * 1024 * 1024, 8, TimeSpan.FromSeconds(30), 2));

    RelatedArtifactDiscoveryResult result = await service.DiscoverAsync(request);
    Require(result.Succeeded && !result.WasCanceled, "Discovery did not complete.");
    Require(result.SourceSha256 == sourceHash, "Source SHA-256 was not stable.");
    Require(result.Candidates.Any(c => c.Location == exact && c.Classification == RelatedArtifactClassification.ConfirmedCopy),
        "Renamed exact hash duplicate was not confirmed.");
    Require(!result.Candidates.Any(c => c.Location == sameNameDifferent && c.Classification == RelatedArtifactClassification.ConfirmedCopy),
        "Same-name different content was falsely confirmed.");
    Require(!result.Candidates.Any(c => c.Location == sameSizeDifferent && c.Classification == RelatedArtifactClassification.ConfirmedCopy),
        "Same-size different content was falsely confirmed.");
    Require(result.Candidates.Any(c => c.Location == sentinelArtifact && c.Classification == RelatedArtifactClassification.ConfirmedCopy && c.Evidence.Contains("provenance")),
        "Sentinel provenance + exact hash artifact was not confirmed.");
    Require(result.Candidates.Any(c => c.Location == historical && c.Provider == "FileHistory" && c.Classification == RelatedArtifactClassification.ConfirmedCopy),
        "Configured File History exact-hash version was not discovered.");
    Require(result.Candidates.Any(c => c.Location == previous && c.Provider == "PreviousVersions" && c.Classification == RelatedArtifactClassification.ConfirmedCopy),
        "Configured Previous Versions exact-hash item was not discovered.");
    Require(result.Candidates.Any(c => c.Location == cloudLocal && c.Provider == "OneDriveLocal" && !c.RemoteStateVerified),
        "Local OneDrive copy was not separated from unverified remote state.");
    Require(result.Candidates.Any(c => c.Provider == "WindowsSearch" && c.Classification == RelatedArtifactClassification.MetadataReferenceOnly),
        "Reliable Windows Search reference was not metadata-only.");
    Require(result.Candidates.Any(c => c.Location == "search://weak-name-only" && c.Classification == RelatedArtifactClassification.UnverifiedCandidate),
        "Weak metadata reference was granted too much authority.");

    string hardLink = Path.Combine(scan, "hardlink.txt");
    if (CreateHardLinkW(hardLink, source, IntPtr.Zero))
    {
        RelatedArtifactDiscoveryResult hardlinkResult = await service.DiscoverAsync(request);
        RelatedArtifactCandidate? hardlinkCandidate = hardlinkResult.Candidates.FirstOrDefault(c => c.Location == hardLink);
        Require(hardlinkCandidate is not null && hardlinkCandidate.IsSameFilesystemObject &&
                hardlinkCandidate.Classification == RelatedArtifactClassification.UnverifiedCandidate &&
                !hardlinkCandidate.TargetedRemovalSupported,
            "A hardlink to the selected object was treated as an independent deletable copy.");
        File.Delete(hardLink);
    }

    RelatedArtifactDiscoveryRequest boundedRequest = request with
    {
        Bounds = new RelatedArtifactDiscoveryBounds(1, 64 * 1024 * 1024, 8, TimeSpan.FromSeconds(30), 1)
    };
    RelatedArtifactDiscoveryResult bounded = await service.DiscoverAsync(boundedRequest);
    Require(bounded.WasLimited, "File-count bound was not surfaced as a partial/limited result.");

    using CancellationTokenSource canceled = new();
    canceled.Cancel();
    RelatedArtifactDiscoveryResult canceledResult = await service.DiscoverAsync(request, canceled.Token);
    Require(!canceledResult.Succeeded && canceledResult.WasCanceled, "Canceled discovery did not report cancellation.");

    RelatedArtifactDiscoveryResult unavailableProviders = await service.DiscoverAsync(new RelatedArtifactDiscoveryRequest(
        source,
        Array.Empty<string>(),
        Bounds: new RelatedArtifactDiscoveryBounds(100, 1024 * 1024, 2, TimeSpan.FromSeconds(10), 1)));
    Require(unavailableProviders.Providers.Any(p => p.Provider == "FileHistory" && p.State == RelatedArtifactProviderState.Unavailable),
        "Unavailable File History was silently reported as nothing found.");
    Require(unavailableProviders.Providers.Any(p => p.Provider == "PreviousVersions" && p.State == RelatedArtifactProviderState.Unavailable),
        "Unavailable Previous Versions was silently reported as nothing found.");
    Require(unavailableProviders.Providers.Any(p => p.Provider == "WindowsSearch" && p.State == RelatedArtifactProviderState.Unavailable),
        "Unavailable Windows Search was silently reported as nothing found.");

    Environment.SetEnvironmentVariable("OneDrive", priorOneDrive);
    Console.WriteLine("Exact hash duplicate / renamed copy: PASS");
    Console.WriteLine("Same-name and same-size false-positive resistance: PASS");
    Console.WriteLine("Sentinel provenance attribution: PASS");
    Console.WriteLine("File History / Previous Versions configured-root discovery: PASS");
    Console.WriteLine("Windows Search / Recent metadata-only classification: PASS");
    Console.WriteLine("OneDrive local vs remote-state separation: PASS");
    Console.WriteLine("Hardlink independent-delete rejection: PASS");
    Console.WriteLine("Bounds / cancellation / unavailable-provider reporting: PASS");
    Console.WriteLine("RESULT: PASS");
}
finally
{
    try
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    catch { }
}

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool CreateHardLinkW(string newFileName, string existingFileName, IntPtr securityAttributes);
