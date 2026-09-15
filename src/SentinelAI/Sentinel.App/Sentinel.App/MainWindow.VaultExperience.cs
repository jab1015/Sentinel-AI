using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sentinel.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Sentinel.App;

public sealed partial class MainWindow
{
    private const int MaximumVaultFolderFiles = 100_000;
    private const int MaximumVaultFolderDirectories = 100_000;

    private async Task AddExplorerSelectionToVaultAsync(string path, FrameworkElement rootElement)
    {
        bool isDirectory = Directory.Exists(path); bool isFile = File.Exists(path);
        if (!isDirectory && !isFile) { await ShowPrivacyMessageAsync(rootElement, "Vault selection is unavailable", "The selected file or folder is no longer available. Nothing was changed."); return; }
        string[] files; string[] directories = Array.Empty<string>();
        if (isDirectory) { if (!TryDiscoverVaultFolder(path, out files, out directories, out string discoveryError)) { await ShowPrivacyMessageAsync(rootElement, "Folder cannot be added", discoveryError); return; } }
        else files = new[] { path };
        long totalBytes = 0; try { totalBytes = files.Sum(file => new FileInfo(file).Length); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        ContentDialog confirmation = new() { Title = isDirectory ? "Move folder to Sentinel Vault" : "Move file to Sentinel Vault", Content = isDirectory ? $"Folder:\n{path}\n\nFiles: {files.Length:N0}\nFolders: {directories.Length:N0}\nSize: {FormatBytes(totalBytes)}\n\nSentinel has checked the selected tree for reparse points and will not follow links outside it. Files are encrypted and verified before any readable source is retired. File hierarchy is stored in the authenticated Vault index. Empty-folder-only structure is not yet persisted, so an entirely empty folder is left unchanged." : $"File:\n{path}\n\nSize: {FormatBytes(totalBytes)}\n\nSentinel will encrypt and verify the file inside its private Vault. Only after the Vault item is verified will Sentinel retire the exact readable source file.", PrimaryButtonText = "Move to Vault", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = rootElement.XamlRoot };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        if (isDirectory && files.Length == 0) { await ShowPrivacyMessageAsync(rootElement, "Empty folder kept unchanged", "Sentinel verified that this folder contains no files. The current Vault format does not yet persist empty-folder-only structure, so Sentinel did not remove or pretend to move the source folder."); return; }
        using PremiumPrivacyEntitlementClient entitlement = new(); PremiumPrivacyAuthorizationResult authorized = await entitlement.AuthorizeOneShotAsync(PremiumPrivacyEntitlementClient.VaultScope).ConfigureAwait(true);
        if (!authorized.Succeeded) { await ShowEntitlementFailureAsync(rootElement, authorized).ConfigureAwait(true); return; }
        PersistedVaultSessionResult open = await OpenOrCreateVaultAsync(rootElement).ConfigureAwait(true);
        if (!open.Succeeded || open.Session is null) { if (!string.Equals(open.Code, "Canceled", StringComparison.Ordinal)) await ShowPrivacyMessageAsync(rootElement, "Sentinel Vault could not open", open.Message + "\n\nStatus: " + open.Code).ConfigureAwait(true); return; }
        List<SecureDeleteValidatedTarget> identities = new(files.Length);
        foreach (string source in files) { SecureDeleteTargetValidationResult identity = SecureDeleteTargetValidator.Validate(source); if (!identity.Succeeded) { await ShowPrivacyMessageAsync(rootElement, "Vault move could not start", $"Sentinel could not bind every source to an exact filesystem identity before writing to the Vault. Nothing has been retired.\n\nSource:\n{source}\n\n{identity.Message}"); open.Session.Dispose(); return; } identities.Add(identity.Target); }
        int protectedCount = 0; long protectedBytes = 0; string? failure = null; List<VaultAddItemResult> staged = new(files.Length); Guid? collectionId = isDirectory ? Guid.NewGuid() : null; string rootFullPath = Path.GetFullPath(path);
        using (PersistedVaultSession session = open.Session)
        {
            for (int index = 0; index < files.Length; index++)
            {
                string? relativePath = isDirectory ? Path.GetRelativePath(rootFullPath, files[index]) : null;
                VaultAddItemResult added = await session.Items.AddFileAsync(files[index], relativePath, collectionId, Path.GetFileName(files[index])).ConfigureAwait(true);
                if (!added.Succeeded) { failure = $"Sentinel stopped before source retirement because it could not commit a verified Vault item for:\n{files[index]}\n\nStatus: {added.Code}\n\nNo source file has been intentionally retired by this operation."; break; }
                staged.Add(added); protectedBytes += added.PlaintextBytes;
            }
            if (failure is null)
            {
                for (int index = 0; index < files.Length; index++) { if (!TryRetireVaultSource(identities[index], out string retirementStatus)) { failure = $"All {staged.Count:N0} file(s) are safely stored and verified in the Vault, but Sentinel could not prove safe retirement of this readable source:\n{files[index]}\n\n{retirementStatus}\n\nThat source was kept for safety."; break; } protectedCount++; }
            }
        }
        if (failure is null && isDirectory)
        {
            try
            {
                foreach (string directory in directories.OrderByDescending(value => value.Length))
                {
                    if (!Directory.Exists(directory)) continue;
                    if ((File.GetAttributes(directory) & System.IO.FileAttributes.ReparsePoint) != 0) { failure = "Protected files are committed, but Sentinel detected that an empty source-folder path changed into a reparse point during cleanup. It was not removed."; break; }
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
                }
                if (failure is null && Directory.Exists(path))
                {
                    if ((File.GetAttributes(path) & System.IO.FileAttributes.ReparsePoint) != 0) failure = "Protected files are committed, but the selected root changed into a reparse point during cleanup. Sentinel did not remove it.";
                    else if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failure = "All readable source files were verified as retired, but Sentinel could not remove one or more empty source folders. The protected Vault data remains committed."; }
        }
        if (failure is null) await ShowPrivacyMessageAsync(rootElement, "Moved to Sentinel Vault", $"Sentinel encrypted, authenticated, and committed {staged.Count:N0} file(s), then verified retirement of each readable source file.\n\nProtected: {FormatBytes(protectedBytes)}\n\nOpen Vault from the Sentinel dashboard to view or restore protected items.").ConfigureAwait(true);
        else await ShowPrivacyMessageAsync(rootElement, "Vault move needs attention", $"Vault items committed: {staged.Count:N0} of {files.Length:N0}\nReadable sources verified retired: {protectedCount:N0} of {files.Length:N0}\nProtected: {FormatBytes(protectedBytes)}\n\n{failure}").ConfigureAwait(true);
    }

    private static bool TryDiscoverVaultFolder(string rootPath, out string[] files, out string[] directories, out string error)
    {
        files = Array.Empty<string>(); directories = Array.Empty<string>(); error = string.Empty;
        try
        {
            string root = Path.GetFullPath(rootPath); if ((File.GetAttributes(root) & System.IO.FileAttributes.ReparsePoint) != 0) { error = "Sentinel will not import a reparse-point folder because its contents can resolve outside the selected folder."; return false; }
            List<string> discoveredFiles = new(); List<string> discoveredDirectories = new(); Stack<string> pending = new(); pending.Push(root);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    System.IO.FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & System.IO.FileAttributes.ReparsePoint) != 0) { error = $"The selected folder contains a reparse point. Sentinel left the entire source tree unchanged rather than following an ambiguous link:\n{entry}"; return false; }
                    if ((attributes & System.IO.FileAttributes.Directory) != 0) { if (discoveredDirectories.Count >= MaximumVaultFolderDirectories) { error = $"This folder contains more than {MaximumVaultFolderDirectories:N0} subfolders, which exceeds Sentinel's bounded Vault import limit. Nothing was moved."; return false; } discoveredDirectories.Add(entry); pending.Push(entry); }
                    else { if (discoveredFiles.Count >= MaximumVaultFolderFiles) { error = $"This folder contains more than {MaximumVaultFolderFiles:N0} files, which exceeds Sentinel's bounded Vault import limit. Nothing was moved."; return false; } discoveredFiles.Add(entry); }
                }
            }
            files = discoveredFiles.ToArray(); directories = discoveredDirectories.ToArray(); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException) { error = "Sentinel could not enumerate and validate the entire folder tree safely. Nothing was moved into the Vault."; return false; }
    }

    private static bool TryRetireVaultSource(SecureDeleteValidatedTarget target, out string status)
    {
        SecureDeleteCoordinator coordinator = new(); SecureDeletePreparationResult prepared = coordinator.Prepare(target); if (!prepared.Succeeded || prepared.Authorization is null) { status = prepared.Message; return false; }
        string journalRoot = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Privacy", "SecureDeleteJournal"); SecureDeleteOperationJournal journal = new(journalRoot); SecureDeleteExactObjectExecutor executor = new(coordinator, journal); SecureDeleteExecutionResult result = executor.Execute(prepared.Authorization);
        if (result.Succeeded && result.PrimaryRemoval == SecureDeletePrimaryRemovalStatus.Verified) { status = "Verified removed"; return true; } status = $"Status: {result.Code}. {result.Message}"; return false;
    }

    private async Task<PersistedVaultSessionResult> OpenOrCreateVaultAsync(FrameworkElement rootElement)
    {
        string vaultRoot = Path.Combine(ApplicationData.Current.LocalFolder.Path, "SentinelVault"); PersistedSentinelVaultService persistence = new(vaultRoot); if (persistence.Exists) return await persistence.OpenCurrentUserAsync().ConfigureAwait(true);
        using RecoveryKeyMaterial recovery = RecoveryKeyMaterial.Generate(); string recoveryText = recovery.ToDisplayString(); TextBox keyBox = new() { Header = "Vault recovery key", Text = recoveryText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, IsSpellCheckEnabled = false };
        StackPanel panel = new() { Spacing = 10, MinWidth = 430 }; panel.Children.Add(new TextBlock { Text = "Save this recovery key somewhere separate from this PC. It is the independent recovery path to your Vault. Sentinel does not upload or retain the plaintext recovery key.", TextWrapping = TextWrapping.Wrap }); panel.Children.Add(keyBox); panel.Children.Add(new TextBlock { Text = "Click the key box and press Ctrl+A, then Ctrl+C to copy it.", TextWrapping = TextWrapping.Wrap });
        ContentDialog dialog = new() { Title = "Create Sentinel Vault", Content = panel, PrimaryButtonText = "I saved the key", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = rootElement.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return PersistedVaultSessionResult.Fail("Canceled", "Vault creation was canceled."); return await persistence.CreateAsync(recovery).ConfigureAwait(true);
    }

    private async void OpenVaultButton_Click(object sender, RoutedEventArgs e)
    {
        FrameworkElement rootElement = (FrameworkElement)Content;
        await WaitForXamlRootAsync(rootElement).ConfigureAwait(true);
        await ShowVaultManagerAsync(rootElement).ConfigureAwait(true);
    }

    private async Task ShowVaultManagerAsync(FrameworkElement rootElement)
    {
        string vaultRoot = Path.Combine(ApplicationData.Current.LocalFolder.Path, "SentinelVault");

        while (true)
        {
            PersistedSentinelVaultService persistence = new(vaultRoot);
            if (!persistence.Exists)
            {
                ContentDialog firstUse = new()
                {
                    Title = "Sentinel Vault",
                    Content = "Your Vault has not been created yet. Choose a file or folder below. Sentinel will show your independent recovery key before creating the Vault, then encrypt and verify the selected content before retiring the readable source.",
                    PrimaryButtonText = "Add file",
                    SecondaryButtonText = "Add folder",
                    CloseButtonText = "Close",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = rootElement.XamlRoot
                };
                ContentDialogResult firstChoice = await firstUse.ShowAsync();
                string? firstPath = firstChoice switch
                {
                    ContentDialogResult.Primary => await PickVaultFileAsync().ConfigureAwait(true),
                    ContentDialogResult.Secondary => await PickVaultFolderAsync().ConfigureAwait(true),
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(firstPath)) return;
                await AddExplorerSelectionToVaultAsync(firstPath, rootElement).ConfigureAwait(true);
                if (!new PersistedSentinelVaultService(vaultRoot).Exists) return;
                continue;
            }

            PersistedVaultSessionResult open = await persistence.OpenCurrentUserAsync().ConfigureAwait(true);
            if (!open.Succeeded || open.Session is null)
            {
                await ShowPrivacyMessageAsync(rootElement, "Vault could not be opened", open.Message + "\n\nStatus: " + open.Code).ConfigureAwait(true);
                return;
            }

            string requestedAction = string.Empty;
            VaultMetadataItem? selectedForExport = null;
            using (PersistedVaultSession session = open.Session)
            {
                VaultRecoveryResult recovered = await session.Items.RecoverPendingAsync().ConfigureAwait(true);
                if (!recovered.Succeeded)
                {
                    await ShowPrivacyMessageAsync(rootElement, "Vault needs recovery review", "Sentinel found an incomplete Vault transaction that could not be resolved automatically.\n\nStatus: " + recovered.Code).ConfigureAwait(true);
                    return;
                }

                VaultCommittedItemsResult items = await session.Items.ListCommittedItemsAsync().ConfigureAwait(true);
                if (!items.Succeeded)
                {
                    await ShowPrivacyMessageAsync(rootElement, "Vault items unavailable", "Sentinel could not authenticate the Vault item index.\n\nStatus: " + items.Code).ConfigureAwait(true);
                    return;
                }

                long total = items.Items.Sum(item => item.PlaintextBytes);
                StackPanel panel = new() { Spacing = 12, MinWidth = 620 };
                panel.Children.Add(new TextBlock
                {
                    Text = items.Items.Count == 0 ? "Your Vault is empty." : $"{items.Items.Count:N0} protected item(s) • {FormatBytes(total)}",
                    FontSize = 20,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                panel.Children.Add(new TextBlock
                {
                    Text = "Protected items stay encrypted in Sentinel's private app storage. Files imported from the same folder remain linked as one authenticated Vault collection, so restoring any member restores the whole folder structure.",
                    TextWrapping = TextWrapping.Wrap
                });

                ListView itemList = new()
                {
                    SelectionMode = ListViewSelectionMode.Single,
                    MaxHeight = 330,
                    MinHeight = items.Items.Count == 0 ? 80 : 160
                };
                foreach (VaultMetadataItem item in items.Items.OrderByDescending(i => i.AddedUnixMs))
                {
                    string name = string.IsNullOrWhiteSpace(item.DisplayName) ? $"Protected item {item.ItemId.ToString("N")[..8]}" : item.DisplayName;
                    string hierarchy = !string.IsNullOrWhiteSpace(item.RelativePath) ? item.RelativePath : item.OriginalPath ?? "Original location unavailable (legacy item)";
                    string added = item.AddedUnixMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(item.AddedUnixMs).LocalDateTime.ToString("MMM d, yyyy h:mm tt") : "Legacy item";
                    string collectionLabel = item.CollectionId.HasValue ? " • Folder collection" : string.Empty;
                    StackPanel row = new() { Spacing = 2, Padding = new Thickness(4, 6, 4, 6) };
                    row.Children.Add(new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                    row.Children.Add(new TextBlock { Text = hierarchy, Opacity = 0.78, TextWrapping = TextWrapping.Wrap });
                    row.Children.Add(new TextBlock { Text = $"{FormatBytes(item.PlaintextBytes)} • Added {added}{collectionLabel}", Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
                    itemList.Items.Add(new ListViewItem { Content = row, Tag = item, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                }
                panel.Children.Add(itemList);

                StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
                Button addFileButton = new() { Content = "Add file" };
                Button addFolderButton = new() { Content = "Add folder" };
                Button exportButton = new() { Content = "Restore copy", IsEnabled = false };
                actions.Children.Add(addFileButton);
                actions.Children.Add(addFolderButton);
                actions.Children.Add(exportButton);
                panel.Children.Add(actions);
                panel.Children.Add(new TextBlock
                {
                    Text = "Add file/folder is a verified move into the Vault: Sentinel commits and verifies encrypted Vault data before retiring the exact readable source. Restore never overwrites an existing file or folder and does not require a current subscription.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.78
                });

                ContentDialog manager = new()
                {
                    Title = "Sentinel Vault",
                    Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
                    CloseButtonText = "Close",
                    XamlRoot = rootElement.XamlRoot
                };

                itemList.SelectionChanged += (_, _) =>
                {
                    if (itemList.SelectedItem is ListViewItem selected && selected.Tag is VaultMetadataItem item)
                    {
                        exportButton.IsEnabled = true;
                        exportButton.Content = item.CollectionId.HasValue ? "Restore folder" : "Restore copy";
                    }
                    else
                    {
                        exportButton.IsEnabled = false;
                        exportButton.Content = "Restore copy";
                    }
                };
                addFileButton.Click += (_, _) => { requestedAction = "add-file"; manager.Hide(); };
                addFolderButton.Click += (_, _) => { requestedAction = "add-folder"; manager.Hide(); };
                exportButton.Click += (_, _) =>
                {
                    if (itemList.SelectedItem is ListViewItem listItem && listItem.Tag is VaultMetadataItem item)
                    {
                        selectedForExport = item;
                        requestedAction = "export";
                        manager.Hide();
                    }
                };

                await manager.ShowAsync();
            }

            if (string.IsNullOrEmpty(requestedAction)) return;

            if (requestedAction is "add-file" or "add-folder")
            {
                string? path = requestedAction == "add-file"
                    ? await PickVaultFileAsync().ConfigureAwait(true)
                    : await PickVaultFolderAsync().ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(path))
                    await AddExplorerSelectionToVaultAsync(path, rootElement).ConfigureAwait(true);
                continue;
            }

            if (requestedAction == "export" && selectedForExport is not null)
            {
                string? destinationFolder = await PickVaultExportFolderAsync().ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(destinationFolder)) continue;

                if (selectedForExport.CollectionId.HasValue)
                {
                    await RestoreVaultCollectionAsync(vaultRoot, selectedForExport, destinationFolder, rootElement).ConfigureAwait(true);
                    continue;
                }

                string fileName = GetVaultExportFileName(selectedForExport);
                string destination = Path.Combine(destinationFolder, fileName);
                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    await ShowPrivacyMessageAsync(rootElement, "Restore destination already exists", $"Sentinel will not overwrite an existing file or folder.\n\nDestination:\n{destination}\n\nRename or move the existing item, or choose a different folder, then try Restore copy again.").ConfigureAwait(true);
                    continue;
                }

                PersistedVaultSessionResult exportOpen = await new PersistedSentinelVaultService(vaultRoot).OpenCurrentUserAsync().ConfigureAwait(true);
                if (!exportOpen.Succeeded || exportOpen.Session is null)
                {
                    await ShowPrivacyMessageAsync(rootElement, "Vault could not be opened for restore", exportOpen.Message + "\n\nStatus: " + exportOpen.Code).ConfigureAwait(true);
                    continue;
                }

                VaultExportResult exportResult;
                using (PersistedVaultSession exportSession = exportOpen.Session)
                {
                    VaultItemExportService exporter = new(vaultRoot, exportSession.Vault, exportSession.Items);
                    exportResult = await exporter.ExportAsync(selectedForExport.ItemId, destination).ConfigureAwait(true);
                }

                if (exportResult.Succeeded)
                {
                    await ShowPrivacyMessageAsync(rootElement, "Vault file restored", $"Sentinel authenticated the protected Vault item and restored a verified plaintext copy.\n\nRestored file:\n{exportResult.DestinationPath}\n\nRestored: {FormatBytes(exportResult.PlaintextBytes)}\n\nThe encrypted Vault item remains protected in the Vault.").ConfigureAwait(true);
                }
                else
                {
                    await ShowPrivacyMessageAsync(rootElement, "Vault restore did not complete", $"Sentinel did not report a successful restore.\n\nStatus: {exportResult.Code}\nDestination:\n{exportResult.DestinationPath}\n\nPlaintext output remains: {(exportResult.OutputRemains ? "YES — review the destination" : "NO")}").ConfigureAwait(true);
                }
                continue;
            }
        }
    }

    private async Task RestoreVaultCollectionAsync(string vaultRoot, VaultMetadataItem selected, string destinationParent, FrameworkElement rootElement)
    {
        PersistedVaultSessionResult open = await new PersistedSentinelVaultService(vaultRoot).OpenCurrentUserAsync().ConfigureAwait(true);
        if (!open.Succeeded || open.Session is null)
        {
            await ShowPrivacyMessageAsync(rootElement, "Vault could not be opened for folder restore", open.Message + "\n\nStatus: " + open.Code).ConfigureAwait(true);
            return;
        }

        using PersistedVaultSession session = open.Session;
        VaultCommittedItemsResult listed = await session.Items.ListCommittedItemsAsync().ConfigureAwait(true);
        if (!listed.Succeeded || !selected.CollectionId.HasValue)
        {
            await ShowPrivacyMessageAsync(rootElement, "Vault folder metadata unavailable", "Sentinel could not authenticate the complete folder collection, so it did not restore a partial folder.").ConfigureAwait(true);
            return;
        }

        VaultMetadataItem[] collection = listed.Items
            .Where(item => item.CollectionId == selected.CollectionId)
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string destinationRoot = string.Empty;
        Dictionary<Guid, string> destinations = new();
        string planError = string.Empty;
        if (collection.Length == 0 || !TryBuildVaultCollectionRestorePlan(collection, destinationParent, out destinationRoot, out destinations, out planError))
        {
            await ShowPrivacyMessageAsync(rootElement, "Vault folder cannot be restored", string.IsNullOrWhiteSpace(planError) ? "Sentinel could not build a safe restore plan for this folder collection." : planError).ConfigureAwait(true);
            return;
        }

        try
        {
            Directory.CreateDirectory(destinationRoot);
            if ((File.GetAttributes(destinationRoot) & System.IO.FileAttributes.ReparsePoint) != 0)
                throw new IOException("The newly created restore root resolved as a reparse point.");

            foreach (string directory in destinations.Values
                         .Select(Path.GetDirectoryName)
                         .OfType<string>()
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(value => value.Length))
            {
                Directory.CreateDirectory(directory);
                if ((File.GetAttributes(directory) & System.IO.FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A restore directory resolved as a reparse point.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowPrivacyMessageAsync(rootElement, "Vault folder restore could not start", "Sentinel could not create and verify the new destination folder safely. No Vault item was removed or changed.").ConfigureAwait(true);
            return;
        }

        VaultItemExportService exporter = new(vaultRoot, session.Vault, session.Items);
        int restored = 0;
        long restoredBytes = 0;
        foreach (VaultMetadataItem item in collection)
        {
            VaultExportResult result = await exporter.ExportAsync(item.ItemId, destinations[item.ItemId]).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                await ShowPrivacyMessageAsync(rootElement, "Vault folder restore needs attention", $"Sentinel restored {restored:N0} of {collection.Length:N0} file(s) before a verified export failed. Existing output was left in the new destination folder rather than risk deleting the wrong filesystem objects.\n\nDestination:\n{destinationRoot}\n\nStatus: {result.Code}\n\nThe encrypted Vault collection remains unchanged and protected.").ConfigureAwait(true);
                return;
            }
            restored++;
            restoredBytes += result.PlaintextBytes;
        }

        await ShowPrivacyMessageAsync(rootElement, "Vault folder restored", $"Sentinel authenticated the complete Vault collection and recreated its folder hierarchy as a verified plaintext copy.\n\nRestored folder:\n{destinationRoot}\n\nFiles: {restored:N0}\nRestored: {FormatBytes(restoredBytes)}\n\nThe encrypted Vault collection remains protected in the Vault.").ConfigureAwait(true);
    }

    private static bool TryBuildVaultCollectionRestorePlan(
        IReadOnlyCollection<VaultMetadataItem> collection,
        string destinationParent,
        out string destinationRoot,
        out Dictionary<Guid, string> destinations,
        out string error)
    {
        destinationRoot = string.Empty;
        destinations = new Dictionary<Guid, string>();
        error = string.Empty;
        try
        {
            string parent = Path.GetFullPath(destinationParent);
            if (!Directory.Exists(parent) || (File.GetAttributes(parent) & System.IO.FileAttributes.ReparsePoint) != 0)
            {
                error = "Sentinel will only restore a folder into an existing normal directory that is not a reparse point.";
                return false;
            }

            VaultMetadataItem? sample = collection.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.OriginalPath) && !string.IsNullOrWhiteSpace(item.RelativePath));
            if (sample is null || !TryDeriveVaultCollectionRootName(sample, out string rootName))
            {
                error = "This Vault collection does not contain enough authenticated source metadata to reconstruct the original folder safely.";
                return false;
            }

            destinationRoot = Path.GetFullPath(Path.Combine(parent, rootName));
            if (File.Exists(destinationRoot) || Directory.Exists(destinationRoot))
            {
                error = $"Sentinel will not merge into or overwrite an existing folder.\n\nDestination:\n{destinationRoot}\n\nRename the existing folder or choose a different restore location.";
                return false;
            }

            string rootPrefix = Path.TrimEndingDirectorySeparator(destinationRoot) + Path.DirectorySeparatorChar;
            HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
            foreach (VaultMetadataItem item in collection)
            {
                string relative = item.RelativePath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
                    relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                        .Any(segment => segment is "." or ".."))
                {
                    error = "The authenticated Vault collection contains an invalid relative path. Sentinel refused to create a partial or escaped restore.";
                    return false;
                }

                string destination = Path.GetFullPath(Path.Combine(destinationRoot, relative));
                if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !unique.Add(destination) ||
                    File.Exists(destination) || Directory.Exists(destination))
                {
                    error = "The Vault collection contains a path collision or a path outside the new restore folder. Sentinel did not restore any files.";
                    return false;
                }
                destinations[item.ItemId] = destination;
            }

            return destinations.Count == collection.Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "Sentinel could not validate a safe destination for the complete Vault folder collection.";
            return false;
        }
    }

    private static bool TryDeriveVaultCollectionRootName(VaultMetadataItem item, out string rootName)
    {
        rootName = string.Empty;
        try
        {
            string original = Path.GetFullPath(item.OriginalPath!);
            string relative = item.RelativePath!;
            string[] segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return false;

            string? cursor = original;
            for (int i = 0; i < segments.Length; i++)
            {
                cursor = Path.GetDirectoryName(cursor);
                if (string.IsNullOrWhiteSpace(cursor)) return false;
            }

            rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(cursor!));
            return !string.IsNullOrWhiteSpace(rootName) && rootName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private async Task<string?> PickVaultFileAsync()
    {
        FileOpenPicker picker = new()
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        StorageFile? file = await picker.PickSingleFileAsync();
        return file is null || string.IsNullOrWhiteSpace(file.Path) ? null : file.Path;
    }

    private async Task<string?> PickVaultFolderAsync()
    {
        FolderPicker picker = new()
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder is null || string.IsNullOrWhiteSpace(folder.Path) ? null : folder.Path;
    }

    private async Task<string?> PickVaultExportFolderAsync()
    {
        FolderPicker picker = new()
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder is null || string.IsNullOrWhiteSpace(folder.Path) ? null : folder.Path;
    }

    private void InitializePicker(object picker)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    private static string GetVaultExportFileName(VaultMetadataItem item)
    {
        string candidate = string.IsNullOrWhiteSpace(item.DisplayName)
            ? $"Sentinel-Vault-{item.ItemId:N}.bin"
            : Path.GetFileName(item.DisplayName.Trim());
        if (string.IsNullOrWhiteSpace(candidate) || candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return $"Sentinel-Vault-{item.ItemId:N}.bin";
        return candidate;
    }
}
