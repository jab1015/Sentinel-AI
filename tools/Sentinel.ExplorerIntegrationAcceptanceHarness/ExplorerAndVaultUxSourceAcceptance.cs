using System.Runtime.CompilerServices;

internal static class ExplorerAndVaultUxSourceAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        string root = FindRepoRoot();
        string explorerPath = Path.Combine(root, "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "MainWindow.ExplorerInspection.cs");
        string vaultPath = Path.Combine(root, "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "MainWindow.VaultExperience.cs");
        string retirementPath = Path.Combine(root, "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "Services", "VaultRestoredItemRetirementService.cs");
        string mainWindowPath = Path.Combine(root, "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "MainWindow.xaml");

        Require(File.Exists(explorerPath), "Explorer inspection UI source was not found.");
        Require(File.Exists(vaultPath), "Vault experience source was not found.");
        Require(File.Exists(retirementPath), "Verified restored-item retirement service was not found.");
        Require(File.Exists(mainWindowPath), "Sentinel main-window XAML was not found.");

        string explorer = File.ReadAllText(explorerPath);
        string vault = File.ReadAllText(vaultPath);
        string retirement = File.ReadAllText(retirementPath);
        string mainWindow = File.ReadAllText(mainWindowPath);

        Require(!explorer.Contains("new Windows.Graphics.SizeInt32(1, 1)", StringComparison.Ordinal),
            "Explorer actions regressed to the 1x1 host that produced only a tiny title bar on Windows.");
        Require(explorer.Contains("new Windows.Graphics.SizeInt32(740, 640)", StringComparison.Ordinal),
            "Explorer actions do not have the qualified normally-sized native host marker.");
        Require(explorer.Contains("WHAT SENTINEL CHECKED", StringComparison.Ordinal) &&
                explorer.Contains("WHAT SENTINEL FOUND", StringComparison.Ordinal) &&
                explorer.Contains("not a malware or antivirus scan", StringComparison.OrdinalIgnoreCase),
            "Explorer inspection results lost the required plain-language scope and evidence explanation.");

        Require(vault.Contains("Content = \"Add file\"", StringComparison.Ordinal),
            "Vault manager does not expose Add file.");
        Require(vault.Contains("Content = \"Add folder\"", StringComparison.Ordinal),
            "Vault manager does not expose Add folder.");
        Require(vault.Contains("CreateVaultProgressDialog", StringComparison.Ordinal) &&
                vault.Contains("Encrypting and verifying file", StringComparison.Ordinal) &&
                vault.Contains("Safely retiring source file", StringComparison.Ordinal),
            "Vault folder import no longer exposes bounded encryption/verification/retirement progress.");
        Require(vault.Contains("ListViewSelectionMode.Extended", StringComparison.Ordinal) &&
                vault.Contains("Restore selected", StringComparison.Ordinal),
            "Vault manager lost Windows-style Ctrl/Shift multi-selection restore.");
        Require(vault.Contains("expandedCollections", StringComparison.Ordinal) &&
                vault.Contains("Restore folder", StringComparison.Ordinal) &&
                vault.Contains("TryDeriveVaultCollectionRootName", StringComparison.Ordinal),
            "Vault manager no longer presents authenticated folder collections as expandable/restorable folders.");
        Require(vault.Contains("VaultItemExportService", StringComparison.Ordinal),
            "Vault manager is not wired to the hardened Vault export service.");
        Require(vault.Contains("VaultRestoredItemRetirementService", StringComparison.Ordinal) &&
                vault.Contains("RetireAsync(selectedItems.Keys.ToArray())", StringComparison.Ordinal),
            "Successful restore is not wired to verified Vault-item retirement.");
        Require(retirement.Contains("WriteSnapshotAsync(retiredSnapshot", StringComparison.Ordinal) &&
                retirement.Contains("File.Delete(ciphertextPath)", StringComparison.Ordinal) &&
                retirement.IndexOf("WriteSnapshotAsync(retiredSnapshot", StringComparison.Ordinal) < retirement.IndexOf("File.Delete(ciphertextPath)", StringComparison.Ordinal),
            "Restored-item retirement must commit authenticated metadata removal before ciphertext cleanup.");
        Require(retirement.Contains("verification.Snapshot.Items.Any", StringComparison.Ordinal) &&
                retirement.Contains("CiphertextCleanupNotVerified", StringComparison.Ordinal),
            "Restored-item retirement lost metadata/ciphertext cleanup verification.");
        Require(vault.Contains("WinRT.Interop.InitializeWithWindow.Initialize", StringComparison.Ordinal),
            "Desktop file/folder pickers are not explicitly parented to the Sentinel window.");
        Require(vault.Contains("AddExplorerSelectionToVaultAsync(path, rootElement)", StringComparison.Ordinal),
            "Vault picker path no longer reuses the verified move-to-Vault implementation.");
        Require(vault.Contains("will not overwrite", StringComparison.OrdinalIgnoreCase),
            "Vault restore UI lost no-overwrite user guidance.");

        Require(mainWindow.Contains("<MicaBackdrop", StringComparison.Ordinal),
            "Sentinel main window lost the native WinUI Mica backdrop.");
        Require(mainWindow.Contains("<ColumnDefinition Width=\"224\"/>", StringComparison.Ordinal) &&
                mainWindow.Contains("Security control center", StringComparison.Ordinal) &&
                mainWindow.Contains("Live system state", StringComparison.Ordinal),
            "Sentinel main window no longer has the compact native control-center shell.");
        Require(mainWindow.Contains("x:Name=\"OpenVaultButton\"", StringComparison.Ordinal) &&
                mainWindow.Contains("x:Name=\"OpenQuarantineButton\"", StringComparison.Ordinal),
            "Primary security/privacy navigation actions are missing from the native rail.");
        Require(mainWindow.Contains("Protection Center", StringComparison.Ordinal) &&
                mainWindow.Contains("x:Name=\"ProtectionStatusBorder\"", StringComparison.Ordinal) &&
                mainWindow.Contains("x:Name=\"ProtectionActionCriteriaText\"", StringComparison.Ordinal) &&
                mainWindow.Contains("x:Name=\"ProtectionActionStateText\"", StringComparison.Ordinal),
            "Protection Center/status UX no longer exposes flagged-condition handling and action criteria.");
        Require(mainWindow.Contains("<FontIcon", StringComparison.Ordinal) &&
                !mainWindow.Contains("🔒", StringComparison.Ordinal) &&
                !mainWindow.Contains("🛡", StringComparison.Ordinal),
            "Sentinel main navigation regressed from native glyphs to emoji controls.");
        Require(!mainWindow.Contains("MaxWidth=\"1180\"", StringComparison.Ordinal) &&
                !mainWindow.Contains("Smarter PC protection. Clear answers. Real security.", StringComparison.Ordinal),
            "Sentinel main window regressed toward the centered marketing/landing-page layout.");
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
