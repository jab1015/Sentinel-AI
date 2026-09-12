using Sentinel.App.Services;
using System.Runtime.InteropServices;

internal static class SecureDeleteFoundationAcceptance
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = Path.Combine(Path.GetTempPath(), "SentinelSecureDeleteHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? protectedFixture = null;
        try
        {
            string ordinary = Path.Combine(root, "résumé-源.txt");
            File.WriteAllText(ordinary, "sentinel secure delete identity fixture");

            SecureDeleteTargetValidationResult valid = SecureDeleteTargetValidator.Validate(ordinary);
            Require(valid.Succeeded, "Ordinary exact file was not accepted: " + valid.Code);
            Require(valid.Target.FileLength == new FileInfo(ordinary).Length, "Validated file length did not come from the exact object.");
            Require(SecureDeleteTargetValidator.Revalidate(valid.Target).Succeeded, "Unchanged exact target did not revalidate.");

            string empty = Path.Combine(root, "empty.bin");
            File.WriteAllBytes(empty, Array.Empty<byte>());
            SecureDeleteTargetValidationResult emptyResult = SecureDeleteTargetValidator.Validate(empty);
            Require(emptyResult.Succeeded && emptyResult.Target.FileLength == 0, "Empty file was not safely validated.");

            SecureDeleteTargetValidationResult relative = SecureDeleteTargetValidator.Validate("relative-file.txt");
            Require(!relative.Succeeded && relative.Code == SecureDeleteTargetValidationCode.InvalidPath, "Relative path was accepted.");

            SecureDeleteTargetValidationResult device = SecureDeleteTargetValidator.Validate(@"\\.\PhysicalDrive0");
            Require(!device.Succeeded && device.Code == SecureDeleteTargetValidationCode.UnsupportedNamespace, "Device namespace was accepted.");

            string directory = Path.Combine(root, "folder");
            Directory.CreateDirectory(directory);
            SecureDeleteTargetValidationResult directoryResult = SecureDeleteTargetValidator.Validate(directory);
            Require(!directoryResult.Succeeded && directoryResult.Code == SecureDeleteTargetValidationCode.DirectoryRejected, "Directory target was accepted.");

            string critical = Path.Combine(root, "pagefile.sys");
            File.WriteAllText(critical, "not the real pagefile");
            SecureDeleteTargetValidationResult criticalResult = SecureDeleteTargetValidator.Validate(critical);
            Require(!criticalResult.Succeeded && criticalResult.Code == SecureDeleteTargetValidationCode.SystemCriticalFile, "System-critical filename policy was not enforced.");

            protectedFixture = Path.Combine(AppContext.BaseDirectory, "sentinel-secure-delete-protected-fixture.tmp");
            File.WriteAllText(protectedFixture, "protected application-root fixture");
            SecureDeleteTargetValidationResult protectedResult = SecureDeleteTargetValidator.Validate(protectedFixture);
            Require(!protectedResult.Succeeded && protectedResult.Code == SecureDeleteTargetValidationCode.ProtectedLocation, "Sentinel application root was not protected.");

            string hardlinkSource = Path.Combine(root, "hardlink-source.bin");
            string hardlinkAlias = Path.Combine(root, "hardlink-alias.bin");
            File.WriteAllText(hardlinkSource, "hardlink fixture");
            Require(CreateHardLinkW(hardlinkAlias, hardlinkSource, IntPtr.Zero), "Harness could not create an NTFS hardlink fixture.");
            SecureDeleteTargetValidationResult hardlinkResult = SecureDeleteTargetValidator.Validate(hardlinkSource);
            Require(!hardlinkResult.Succeeded && hardlinkResult.Code == SecureDeleteTargetValidationCode.MultipleLinksRejected, "Multiple-link target was accepted.");

            string swap = Path.Combine(root, "swap.bin");
            string originalMoved = Path.Combine(root, "swap-original.bin");
            File.WriteAllText(swap, "first object");
            SecureDeleteTargetValidationResult beforeSwap = SecureDeleteTargetValidator.Validate(swap);
            Require(beforeSwap.Succeeded, "Swap fixture was not initially valid.");
            File.Move(swap, originalMoved);
            File.WriteAllText(swap, "replacement object");
            SecureDeleteTargetValidationResult afterSwap = SecureDeleteTargetValidator.Revalidate(beforeSwap.Target);
            Require(!afterSwap.Succeeded && afterSwap.Code == SecureDeleteTargetValidationCode.IdentityChanged, "Path replacement retained prior destructive authority.");

            StorageCapabilitySnapshot storage = StorageCapabilityDetector.Detect(valid.Target);
            Require(storage.CanonicalPath.Equals(valid.Target.CanonicalPath, StringComparison.OrdinalIgnoreCase), "Storage detector did not bind its report to the validated path.");
            Require(storage.PhysicalMedia == PhysicalMediaKind.Unknown, "Storage detector guessed a physical media type without reviewed evidence.");
            Require(storage.BitLockerProtected == CapabilityKnowledge.Unknown, "Storage detector guessed BitLocker state.");
            Require(storage.TrimOrUnmapSupported == CapabilityKnowledge.Unknown, "Storage detector guessed TRIM/unmap support.");
            Require(storage.CloudSynchronized == CapabilityKnowledge.Unknown, "Storage detector guessed cloud synchronization state.");

            VerifyReparseSourceContract();

            Console.WriteLine("Secure Delete exact target / hardlink / replacement validation: PASS");
            Console.WriteLine("Secure Delete protected path / namespace policy: PASS");
            Console.WriteLine("Secure Delete conservative storage capability semantics: PASS");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(protectedFixture))
            {
                try { File.Delete(protectedFixture); } catch { }
            }
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void VerifyReparseSourceContract()
    {
        string sourcePath = Path.Combine(Environment.CurrentDirectory,
            "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "Services", "SecureDeleteTargetValidator.cs");
        Require(File.Exists(sourcePath), "Secure Delete validator source was unavailable to the acceptance harness.");
        string source = File.ReadAllText(sourcePath);
        int openReparse = source.IndexOf("FileFlagOpenReparsePoint", StringComparison.Ordinal);
        int reparseCheck = source.IndexOf("FileAttributeReparsePoint", source.IndexOf("attributeInfo.FileAttributes", StringComparison.Ordinal), StringComparison.Ordinal);
        int directoryCheck = source.IndexOf("FileAttributeDirectory", source.IndexOf("attributeInfo.FileAttributes", StringComparison.Ordinal), StringComparison.Ordinal);
        Require(openReparse >= 0 && reparseCheck >= 0 && directoryCheck >= 0 && reparseCheck < directoryCheck,
            "Exact-target validation no longer opens/rejects reparse objects before directory classification.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);
}
