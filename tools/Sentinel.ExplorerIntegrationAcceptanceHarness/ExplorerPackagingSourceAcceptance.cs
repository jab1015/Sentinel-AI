using System.Runtime.CompilerServices;

internal static class ExplorerPackagingSourceAcceptance
{
    private const string Clsid = "6C5E88B7-2A44-4B6D-9A6C-4F1A5C9F6E21";

    [ModuleInitializer]
    internal static void Verify()
    {
        string root = FindRepoRoot();
        string manifestPath = Path.Combine(root, "src", "SentinelAI", "Sentinel.App", "Sentinel.App (Package)", "Package.appxmanifest");
        string nativePath = Path.Combine(root, "src", "SentinelAI", "Sentinel.ExplorerExtension", "SentinelExplorerCommand.cpp");
        string packageProjectPath = Path.Combine(root, "src", "SentinelAI", "Sentinel.App", "Sentinel.App (Package)", "Sentinel.App (Package).wapproj");
        string solutionPath = Path.Combine(root, "src", "SentinelAI", "SentinelAI.slnx");

        Require(File.Exists(manifestPath), "Package manifest was not found.");
        Require(File.Exists(nativePath), "Explorer native command source was not found.");
        Require(File.Exists(packageProjectPath), "Package project was not found.");
        Require(File.Exists(solutionPath), "Solution file was not found.");

        string manifest = File.ReadAllText(manifestPath);
        string native = File.ReadAllText(nativePath);
        string packageProject = File.ReadAllText(packageProjectPath);
        string solution = File.ReadAllText(solutionPath);

        Require(manifest.Contains("Category=\"windows.comServer\"", StringComparison.Ordinal),
            "Explorer command COM server is not package-registered.");
        Require(manifest.Contains("Category=\"windows.fileExplorerContextMenus\"", StringComparison.Ordinal),
            "Explorer context-menu extension is not package-registered.");
        Require(manifest.Contains($"Id=\"{Clsid}\"", StringComparison.OrdinalIgnoreCase) &&
                manifest.Contains($"Clsid=\"{Clsid}\"", StringComparison.OrdinalIgnoreCase),
            "Explorer manifest CLSID registration is inconsistent.");
        Require(manifest.Contains("Path=\"Sentinel.ExplorerExtension.dll\"", StringComparison.Ordinal),
            "Explorer manifest does not point to the packaged native DLL.");
        Require(manifest.Contains("ThreadingModel=\"STA\"", StringComparison.Ordinal),
            "Explorer COM server is not constrained to STA.");
        Require(manifest.Contains("desktop5:ItemType Type=\"*\"", StringComparison.Ordinal),
            "Sentinel Explorer root command is not registered for selected files.");
        Require(manifest.Contains("desktop5:ItemType Type=\"Directory\"", StringComparison.Ordinal),
            "Sentinel Explorer root command is not registered for selected folders.");

        string[] requiredNativeMarkers =
        {
            "IExplorerCommand",
            "IEnumExplorerCommand",
            "ECF_HASSUBCOMMANDS",
            "SIGDN_FILESYSPATH",
            "kMaximumItems = 16",
            "kMaximumRecordBytes = 64 * 1024",
            "Inspect with Sentinel AI",
            "Encrypt for This PC",
            "Encrypt for Sharing...",
            "Decrypt Sentinel File",
            "Add to Sentinel Vault",
            "Secure Delete",
            "\"inspect\"",
            "\"encrypt\"",
            "\"encrypt-share\"",
            "\"decrypt\"",
            "\"vault\"",
            "\"secure-delete\"",
            "CreateFileW",
            "CREATE_NEW",
            "FlushFileBuffers",
            "GetCurrentPackageFamilyName",
            "IApplicationActivationManager",
            "ActivateApplication",
            "--sentinel-explorer-handoff"
        };
        foreach (string marker in requiredNativeMarkers)
            Require(native.Contains(marker, StringComparison.Ordinal), $"Explorer native trust boundary lost required marker: {marker}");

        string[] forbiddenNativeMarkers =
        {
            "Win" + "Http", "Win" + "INet", "Internet" + "Open", "Internet" + "Connect", "Http" + "Client", "URL" + "DownloadToFile",
            "WSA" + "Startup", "socket" + "(", "Store" + "Context", "collections" + "-ticket", "entitle" + "ment", "Privileged" + "Broker",
            "FileEncryption" + "Service", "SentinelVault" + "Service", "SecureDeleteExactObject" + "Executor", "RelatedArtifactDiscovery" + "Service",
            "CryptProtect" + "Data", "B" + "Crypt", "A" + "ES", "Aes" + "Gcm", "SetFileInformation" + "ByHandle"
        };
        foreach (string forbidden in forbiddenNativeMarkers)
            Require(!native.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Explorer native DLL crossed the thin-shell trust boundary: {forbidden}");

        Require(packageProject.Contains("Sentinel.ExplorerExtension\\Sentinel.ExplorerExtension.vcxproj", StringComparison.Ordinal),
            "Package project does not reference the native Explorer extension.");
        Require(packageProject.Contains("Platform=Win32", StringComparison.Ordinal),
            "Package project does not map Sentinel x86 to the native Win32 platform.");
        Require(solution.Contains("Sentinel.ExplorerExtension/Sentinel.ExplorerExtension.vcxproj", StringComparison.Ordinal),
            "Solution does not include the native Explorer extension.");
        Require(solution.Contains("Solution=\"*|x86\" Project=\"Win32\"", StringComparison.Ordinal),
            "Solution x86 -> Win32 native platform mapping is missing.");
    }

    private static string FindRepoRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "src", "SentinelAI")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        throw new InvalidOperationException("Repository root could not be located.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
