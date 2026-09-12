using Sentinel.App.Services;
using System.Runtime.InteropServices;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("Temp cleanup acceptance harness requires Windows.");
    return;
}

string root = Path.Combine(Path.GetTempPath(), "SentinelTempCleanupHarness", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    Assert(HandleBasedTemporaryFileDeletion.TryGetCanonicalDirectoryPath(root, out string canonicalRoot), "canonical root should resolve");

    string stale = Path.Combine(root, "stale.tmp");
    File.WriteAllBytes(stale, new byte[4096]);
    File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-10));
    HandleDeletionResult staleResult = HandleBasedTemporaryFileDeletion.TryDeleteStaleFile(stale, canonicalRoot, DateTime.UtcNow.AddDays(-7));
    Assert(staleResult.Deleted, $"stale file should delete by handle ({staleResult.Reason})");
    Assert(!File.Exists(stale), "deleted stale file should no longer exist after handle close");
    Assert(staleResult.FileBytes == 4096, "exact stale file size should be reported");

    string recent = Path.Combine(root, "recent.tmp");
    File.WriteAllText(recent, "recent");
    HandleDeletionResult recentResult = HandleBasedTemporaryFileDeletion.TryDeleteStaleFile(recent, canonicalRoot, DateTime.UtcNow.AddDays(-7));
    Assert(!recentResult.Deleted && recentResult.Reason == "TooRecent", "recent file must fail closed");
    Assert(File.Exists(recent), "recent file must remain");

    string readOnly = Path.Combine(root, "readonly.tmp");
    File.WriteAllText(readOnly, "protected");
    File.SetLastWriteTimeUtc(readOnly, DateTime.UtcNow.AddDays(-10));
    File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);
    HandleDeletionResult readOnlyResult = HandleBasedTemporaryFileDeletion.TryDeleteStaleFile(readOnly, canonicalRoot, DateTime.UtcNow.AddDays(-7));
    Assert(!readOnlyResult.Deleted && readOnlyResult.Reason == "ProtectedOrReparseObject", "read-only target must be rejected");
    File.SetAttributes(readOnly, FileAttributes.Normal);

    string hardlinkSource = Path.Combine(root, "hardlink-source.tmp");
    string hardlinkAlias = Path.Combine(root, "hardlink-alias.tmp");
    File.WriteAllText(hardlinkSource, "linked");
    File.SetLastWriteTimeUtc(hardlinkSource, DateTime.UtcNow.AddDays(-10));
    if (CreateHardLinkW(hardlinkAlias, hardlinkSource, IntPtr.Zero))
    {
        HandleDeletionResult hardlinkResult = HandleBasedTemporaryFileDeletion.TryDeleteStaleFile(hardlinkSource, canonicalRoot, DateTime.UtcNow.AddDays(-7));
        Assert(!hardlinkResult.Deleted && hardlinkResult.Reason == "MultipleLinksRejected", "multiply-linked target must be rejected");
        Assert(File.Exists(hardlinkSource) && File.Exists(hardlinkAlias), "hard links must remain after rejection");
    }
    else
    {
        Console.WriteLine($"Hard-link case unavailable on this runner (Win32 {Marshal.GetLastWin32Error()}); policy case deferred to runtime matrix.");
    }

    string outside = Path.Combine(Path.GetDirectoryName(root)!, "outside-" + Guid.NewGuid().ToString("N") + ".tmp");
    File.WriteAllText(outside, "outside");
    try
    {
        Assert(!HandleBasedTemporaryFileDeletion.IsStrictlyUnderRoot(outside, canonicalRoot), "outside path must not satisfy canonical root boundary");
    }
    finally { File.Delete(outside); }

    string nested = Path.Combine(root, "nested", "inside.tmp");
    Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
    File.WriteAllText(nested, "inside");
    Assert(HandleBasedTemporaryFileDeletion.IsStrictlyUnderRoot(nested, canonicalRoot), "nested path should satisfy canonical root policy");

    string symlinkTarget = Path.Combine(root, "symlink-target.tmp");
    string symlink = Path.Combine(root, "symlink.tmp");
    File.WriteAllText(symlinkTarget, "target");
    File.SetLastWriteTimeUtc(symlinkTarget, DateTime.UtcNow.AddDays(-10));
    try
    {
        File.CreateSymbolicLink(symlink, symlinkTarget);
        HandleDeletionResult symlinkResult = HandleBasedTemporaryFileDeletion.TryDeleteStaleFile(symlink, canonicalRoot, DateTime.UtcNow.AddDays(-7));
        Assert(!symlinkResult.Deleted && symlinkResult.Reason == "ProtectedOrReparseObject", "reparse target must be rejected");
        Assert(File.Exists(symlinkTarget), "reparse rejection must preserve target");
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
    {
        Console.WriteLine($"Symbolic-link case unavailable on this runner ({ex.GetType().Name}); real reparse-point matrix remains a Windows runtime gate.");
    }

    Console.WriteLine("A14 handle-based temporary cleanup acceptance PASS");
}
finally
{
    try
    {
        foreach (string file in Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : Array.Empty<string>())
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
        }
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
    catch { }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("A14 acceptance failure: " + message);
}

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);
