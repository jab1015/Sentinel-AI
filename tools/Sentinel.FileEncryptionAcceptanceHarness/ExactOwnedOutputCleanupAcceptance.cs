using System.Runtime.CompilerServices;
using Sentinel.App.Services;

internal static class ExactOwnedOutputCleanupAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelExactCleanupHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "owned.tmp");
            OwnedFileIdentity identity;
            using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                stream.WriteByte(0x41);
                stream.Flush(flushToDisk: true);
                Require(ExactOwnedOutputCleanup.TryCapture(stream, path, out identity),
                    "Could not capture stable identity for a Sentinel-owned output.");
            }

            string originalObject = Path.Combine(root, "original-object.tmp");
            File.Move(path, originalObject);
            File.WriteAllText(path, "replacement must survive");

            Require(!ExactOwnedOutputCleanup.TryDeleteSameObject(path, identity),
                "Exact cleanup accepted a replacement object at the same path.");
            Require(File.Exists(path) && File.ReadAllText(path) == "replacement must survive",
                "Exact cleanup damaged the replacement object.");
            Require(File.Exists(originalObject),
                "Exact cleanup damaged the displaced original object.");

            File.Delete(path);
            File.Move(originalObject, path);
            Require(ExactOwnedOutputCleanup.TryDeleteSameObject(path, identity),
                "Exact cleanup refused the original captured filesystem object.");
            Require(!File.Exists(path),
                "Exact cleanup did not remove the original captured filesystem object.");

            Console.WriteLine("Exact failed-output identity cleanup / path-swap resistance: PASS");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
