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

    private sealed record VaultBrowserEntry(
        VaultMetadataItem Item,
        Guid? CollectionId,
        string Name,
        bool IsFolder,
        bool IsChild,
        int FileCount,
        long PlaintextBytes,
        long AddedUnixMs);

    private async Task AddExplorerSelectionToVaultAsync(string path, FrameworkElement rootElement)
    {
        bool isDirectory = Directory.Exists(path);
        bool isFile = File.Exists(path);
        if (!isDirectory && !isFile)
        {
            await ShowPrivacyMessageAsync(rootElement, "Vault selection is unavailable", "The selected file or folder is no longer available. Nothing was changed.");
            return;
        }

        string[] files;
        string[] directories = Array.Empty<string>();
        if (isDirectory)
        {
            if (!TryDiscoverVaultFolder(path, out files, out directories, out string discoveryError))
            {
                await ShowPrivacyMessageAsync(rootElement, "Folder cannot be added", discoveryError);
                return;
            }
        }
        else files = new[] { path };

        long totalBytes = 0;
        try { totalBytes = files.Sum(file => new FileInfo(file).Length); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        ContentDialog confirmation = new()
        {
            Title = isDirectory ? "Move folder to Sentinel Vault" : "Move file to Sentinel Vault",
            Content = isDirectory
                ? $"Folder:\n{path}\n\nFiles: {files.Length:N0}\nFolders: {directories.Length:N0}\nSize: {FormatBytes(totalBytes)}\n\nSentinel has checked the selected tree for reparse points and will not follow links outside it. Files are encrypted and verified before any readable source is retired. The folder remains one authenticated collection in the Vault browser. Empty-folder-only structure is not yet persisted."
                : $"File:\n{path}\n\nSize: {FormatBytes(totalBytes)}\n\nSentinel will encrypt and verify the file inside its private Vault. Only after the Vault item is verified will Sentinel retire the exact readable source file.",
            PrimaryButtonText = "Move to Vault",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = rootElement.XamlRoot
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        if (isDirectory && files.Length == 0)
        {
            await ShowPrivacyMessageAsync(rootElement, "Empty folder kept unchanged", "Sentinel verified that this folder contains no files. The current Vault format does not yet persist empty-folder-only structure, so Sentinel did not remove or pretend to move the source folder.");
            return;
        }

        using PremiumPrivacyEntitlementClient entitlement = new();
        PremiumPrivacyAuthorizationResult authorized = await entitlement.AuthorizeOneShotAsync(PremiumPrivacyEntitlementClient.VaultScope).ConfigureAwait(true);
        if (!authorized.Succeeded)
        {
            await ShowEntitlementFailureAsync(rootElement, authorized).ConfigureAwait(true);
            return;
        }

        PersistedVaultSessionResult open = await OpenOrCreateVaultAsync(rootElement).ConfigureAwait(true);
        if (!open.Succeeded || open.Session is null)
        {
            if (!string.Equals(open.Code, "Canceled", StringComparison.Ordinal))
                await ShowPrivacyMessageAsync(rootElement, "Sentinel Vault could not open", open.Message + "\n\nStatus: " + open.Code).ConfigureAwait(true);
            return;
        }

        List<SecureDeleteValidatedTarget> identities = new(files.Length);
        foreach (string source in files)
        {
            SecureDeleteTargetValidationResult identity = SecureDeleteTargetValidator.Validate(source);
            if (!identity.Succeeded)
            {
                await ShowPrivacyMessageAsync(rootElement, "Vault move could not start", $"Sentinel could not bind every source to an exact filesystem identity before writing to the Vault. Nothing has been retired.\n\nSource:\n{source}\n\n{identity.Message}");
                open.Session.Dispose();
                return;
            }
            identities.Add(identity.Target);
        }

        (ContentDialog progressDialog, ProgressBar progressBar, TextBlock progressText) = CreateVaultProgressDialog(
            rootElement,
            isDirectory ? "Adding folder to Sentinel Vault" : "Adding file to Sentinel Vault",
            Math.Max(1, files.Length * 2));
        _ = progressDialog.ShowAsync();

        int protectedCount = 0;
        long protectedBytes = 0;
        string? failure = null;
        List<VaultAddItemResult> staged = new(files.Length);
        Guid? collectionId = isDirectory ? Guid.NewGuid() : null;
        string rootFullPath = Path.GetFullPath(path);

        try
        {
            using (PersistedVaultSession session = open.Session)
            {
                for (int index = 0; index < files.Length; index++)
                {
                    progressText.Text = $"Encrypting and verifying file {index + 1:N0} of {files.Length:N0}…\n{Path.GetFileName(files[index])}";
                    string? relativePath = isDirectory ? Path.GetRelativePath(rootFullPath, files[index]) : null;
                    VaultAddItemResult added = await session.Items.AddFileAsync(files[index], relativePath, collectionId, Path.GetFileName(files[index])).ConfigureAwait(true);
                    if (!added.Succeeded)
                    {
                        failure = $"Sentinel stopped before source retirement because it could not commit a verified Vault item for:\n{files[index]}\n\nStatus: {added.Code}\n\nNo source file has been intentionally retired by this operation.";
                        break;
                    }
                    staged.Add(added);
                    protectedBytes += added.PlaintextBytes;
                    progressBar.Value = index + 1;
                }

                if (failure is null)
                {
                    for (int index = 0; index < files.Length; index++)
                    {
                        progressText.Text = $"Vault copy verified. Safely retiring source file {index + 1:N0} of {files.Length:N0}…\n{Path.GetFileName(files[index])}";
                        if (!TryRetireVaultSource(identities[index], out string retirementStatus))
                        {
                            failure = $"All {staged.Count:N0} file(s) are safely stored and verified in the Vault, but Sentinel could not prove safe retirement of this readable source:\n{files[index]}\n\n{retirementStatus}\n\nThat source was kept for safety.";
                            break;
                        }
                        protectedCount++;
                        progressBar.Value = files.Length + index + 1;
                    }
                }
            }

            if (failure is null && isDirectory)
            {
                progressText.Text = "Finishing folder cleanup…";
                try
                {
                    foreach (string directory in directories.OrderByDescending(value => value.Length))
                    {
                        if (!Directory.Exists(directory)) continue;
                        if ((File.GetAttributes(directory) & System.IO.FileAttributes.ReparsePoint) != 0)
                        {
                            failure = "Protected files are committed, but Sentinel detected that an empty source-folder path changed into a reparse point during cleanup. It was not removed.";
                            break;
                        }
                        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
                    }
                    if (failure is null && Directory.Exists(path))
                    {
                        if ((File.GetAttributes(path) & System.IO.FileAttributes.ReparsePoint) != 0)
                            failure = "Protected files are committed, but the selected root changed into a reparse point during cleanup. Sentinel did not remove it.";
                        else if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, false);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failure = "All readable source files were verified as retired, but Sentinel could not remove one or more empty source folders. The protected Vault data remains committed.";
                }
            }
        }
        finally
        {
            progressDialog.Hide();
        }

        if (failure is null)
            await ShowPrivacyMessageAsync(rootElement, "Moved to Sentinel Vault", $"Sentinel encrypted, authenticated, and committed {staged.Count:N0} file(s), then verified retirement of each readable source file.\n\nProtected: {FormatBytes(protectedBytes)}\n\nOpen Vault from the Sentinel dashboard to view or restore protected items.").ConfigureAwait(true);
        else
            await ShowPrivacyMessageAsync(rootElement, "Vault move needs attention", $"Vault items committed: {staged.Count:N0} of {files.Length:N0}\nReadable sources verified retired: {protectedCount:N0} of {files.Length:N0}\nProtected: {FormatBytes(protectedBytes)}\n\n{failure}").ConfigureAwait(true);
    }

    private static (ContentDialog Dialog, ProgressBar Bar, TextBlock Text) CreateVaultProgressDialog(FrameworkElement rootElement, string title, double maximum)
    {
        ProgressBar bar = new() { Minimum = 0, Maximum = maximum, Value = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        TextBlock text = new() { Text = "Preparing…", TextWrapping = TextWrapping.Wrap };
        StackPanel panel = new() { Spacing = 12, MinWidth = 430 };
        panel.Children.Add(text);
        panel.Children.Add(bar);
        ContentDialog dialog = new() { Title = title, Content = panel, XamlRoot = rootElement.XamlRoot };
        return (dialog, bar, text);
    }

    private static bool TryDiscoverVaultFolder(string rootPath, out string[] files, out string[] directories, out string error)
    {
        files = Array.Empty<string>();
        directories = Array.Empty<string>();
        error = string.Empty;
        try
        {
            string root = Path.GetFullPath(rootPath);
            if ((File.GetAttributes(root) & System.IO.FileAttributes.ReparsePoint) != 0)
            {
                error = "Sentinel will not import a reparse-point folder because its contents can resolve outside the selected folder.";
                return false;
            }
            List<string> discoveredFiles = new();
            List<string> discoveredDirectories = new();
            Stack<string> pending = new();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    System.IO.FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & System.IO.FileAttributes.ReparsePoint) != 0)
                    {
                        error = $"The selected folder contains a reparse point. Sentinel left the entire source tree unchanged rather than following an ambiguous link:\n{entry}";
                        return false;
                    }
                    if ((attributes & System.IO.FileAttributes.Directory) != 0)
                    {
                        if (discoveredDirectories.Count >= MaximumVaultFolderDirectories)
                        {
                            error = $"This folder contains more than {MaximumVaultFolderDirectories:N0} subfolders, which exceeds Sentinel's bounded Vault import limit. Nothing was moved.";
                            return false;
                        }
                        discoveredDirectories.Add(entry);
                        pending.Push(entry);
                    }
                    else
                    {
                        if (discoveredFiles.Count >= MaximumVaultFolderFiles)
                        {
                            error = $"This folder contains more than {MaximumVaultFolderFiles:N0} files, which exceeds Sentinel's bounded Vault import limit. Nothing was moved.";
                            return false;
                        }
                        discoveredFiles.Add(entry);
                    }
                }
            }
            files = discoveredFiles.ToArray();
            directories = discoveredDirectories.ToArray();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "Sentinel could not enumerate and validate the entire folder tree safely. Nothing was moved into the Vault.";
            return false;
        }
    }

    private static bool TryRetireVaultSource(SecureDeleteValidatedTarget target, out string status)
    {
        SecureDeleteCoordinator coordinator = new();
        SecureDeletePreparationResult prepared = coordinator.Prepare(target);
        if (!prepared.Succeeded || prepared.Authorization is null) { status = prepared.Message; return false; }
        string journalRoot = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Privacy", "SecureDeleteJournal");
        SecureDeleteOperationJournal journal = new(journalRoot);
        SecureDeleteExactObjectExecutor executor = new(coordinator, journal);
        SecureDeleteExecutionResult result = executor.Execute(prepared.Authorization);
        if (result.Succeeded && result.PrimaryRemoval == SecureDeletePrimaryRemovalStatus.Verified) { status = "Verified removed"; return true; }
        status = $"Status: {result.Code}. {result.Message}";
        return false;
    }

    private async Task<PersistedVaultSessionResult> OpenOrCreateVaultAsync(FrameworkElement rootElement)
    {
        string vaultRoot = Path.Combine(ApplicationData.Current.LocalFolder.Path, "SentinelVault");
        PersistedSentinelVaultService persistence = new(vaultRoot);
        if (persistence.Exists) return await persistence.OpenCurrentUserAsync().ConfigureAwait(true);
        using RecoveryKeyMaterial recovery = RecoveryKeyMaterial.Generate();
        string recoveryText = recovery.ToDisplayString();
        TextBox keyBox = new() { Header = "Vault recovery key", Text = recoveryText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, IsSpellCheckEnabled = false };
        StackPanel panel = new() { Spacing = 10, MinWidth = 430 };
        panel.Children.Add(new TextBlock { Text = "Save this recovery key somewhere separate from this PC. It is the independent recovery path to your Vault. Sentinel does not upload or retain the plaintext recovery key.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(keyBox);
        panel.Children.Add(new TextBlock { Text = "Click the key box and press Ctrl+A, then Ctrl+C to copy it.", TextWrapping = TextWrapping.Wrap });
        ContentDialog dialog = new() { Title = "Create Sentinel Vault", Content = panel, PrimaryButtonText = "I saved the key", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = rootElement.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return PersistedVaultSessionResult.Fail("Canceled", "Vault creation was canceled.");
        return await persistence.CreateAsync(recovery).ConfigureAwait(true);
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
            List<VaultBrowserEntry> selectedForRestore = new();
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

                int folderCount = items.Items.Where(item => item.CollectionId.HasValue).Select(item => item.CollectionId!.Value).Distinct().Count();
                int standaloneCount = items.Items.Count(item => !item.CollectionId.HasValue);
                long total = items.Items.Sum(item => item.PlaintextBytes);
                StackPanel panel = new() { Spacing = 12, MinWidth = 650 };
                panel.Children.Add(new TextBlock
                {
                    Text = items.Items.Count == 0 ? "Your Vault is empty." : $"{folderCount:N0} folder(s) • {standaloneCount:N0} standalone file(s) • {FormatBytes(total)}",
                    FontSize = 20,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                panel.Children.Add(new TextBlock
                {
                    Text = "Folders appear as folders. Expand a folder to inspect its protected files. Use Ctrl+click or Shift+click to select multiple files or folders, then choose Restore selected.",
                    TextWrapping = TextWrapping.Wrap
                });

                ListView itemList = new()
                {
                    SelectionMode = ListViewSelectionMode.Extended,
                    MaxHeight = 350,
                    MinHeight = items.Items.Count == 0 ? 80 : 180
                };
                HashSet<Guid> expandedCollections = new();

                void PopulateBrowser()
                {
                    itemList.Items.Clear();
                    IEnumerable<IGrouping<Guid, VaultMetadataItem>> collections = items.Items
                        .Where(item => item.CollectionId.HasValue)
                        .GroupBy(item => item.CollectionId!.Value)
                        .OrderByDescending(group => group.Max(item => item.AddedUnixMs));

                    foreach (IGrouping<Guid, VaultMetadataItem> collection in collections)
                    {
                        VaultMetadataItem sample = collection.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).First();
                        string folderName = TryDeriveVaultCollectionRootName(sample, out string derived) ? derived : $"Protected folder {collection.Key.ToString("N")[..8]}";
                        bool expanded = expandedCollections.Contains(collection.Key);
                        Button expandButton = new() { Content = expanded ? "▾" : "›", Width = 34, Height = 30, Padding = new Thickness(0) };
                        StackPanel folderText = new() { Spacing = 2 };
                        folderText.Children.Add(new TextBlock { Text = folderName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                        folderText.Children.Add(new TextBlock { Text = $"{collection.Count():N0} file(s) • {FormatBytes(collection.Sum(item => item.PlaintextBytes))}", Opacity = 0.72, TextWrapping = TextWrapping.Wrap });
                        Grid row = new() { ColumnSpacing = 8, Padding = new Thickness(4, 6, 4, 6) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.Children.Add(expandButton);
                        Grid.SetColumn(folderText, 1);
                        row.Children.Add(folderText);
                        VaultBrowserEntry folderEntry = new(sample, collection.Key, folderName, true, false, collection.Count(), collection.Sum(item => item.PlaintextBytes), collection.Max(item => item.AddedUnixMs));
                        itemList.Items.Add(new ListViewItem { Content = row, Tag = folderEntry, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                        expandButton.Click += (_, _) =>
                        {
                            if (!expandedCollections.Add(collection.Key)) expandedCollections.Remove(collection.Key);
                            PopulateBrowser();
                        };

                        if (!expanded) continue;
                        foreach (VaultMetadataItem item in collection.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
                        {
                            string name = string.IsNullOrWhiteSpace(item.DisplayName) ? $"Protected item {item.ItemId.ToString("N")[..8]}" : item.DisplayName;
                            StackPanel child = new() { Spacing = 2, Padding = new Thickness(42, 5, 4, 5) };
                            child.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap });
                            child.Children.Add(new TextBlock { Text = item.RelativePath ?? name, Opacity = 0.72, TextWrapping = TextWrapping.Wrap });
                            child.Children.Add(new TextBlock { Text = FormatBytes(item.PlaintextBytes), Opacity = 0.62 });
                            VaultBrowserEntry childEntry = new(item, collection.Key, name, false, true, 1, item.PlaintextBytes, item.AddedUnixMs);
                            itemList.Items.Add(new ListViewItem { Content = child, Tag = childEntry, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                        }
                    }

                    foreach (VaultMetadataItem item in items.Items.Where(item => !item.CollectionId.HasValue).OrderByDescending(item => item.AddedUnixMs))
                    {
                        string name = string.IsNullOrWhiteSpace(item.DisplayName) ? $"Protected item {item.ItemId.ToString("N")[..8]}" : item.DisplayName;
                        string added = item.AddedUnixMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(item.AddedUnixMs).LocalDateTime.ToString("MMM d, yyyy h:mm tt") : "Legacy item";
                        StackPanel row = new() { Spacing = 2, Padding = new Thickness(4, 6, 4, 6) };
                        row.Children.Add(new TextBlock { Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                        row.Children.Add(new TextBlock { Text = item.OriginalPath ?? "Original location unavailable (legacy item)", Opacity = 0.72, TextWrapping = TextWrapping.Wrap });
                        row.Children.Add(new TextBlock { Text = $"{FormatBytes(item.PlaintextBytes)} • Added {added}", Opacity = 0.62, TextWrapping = TextWrapping.Wrap });
                        itemList.Items.Add(new ListViewItem { Content = row, Tag = new VaultBrowserEntry(item, null, name, false, false, 1, item.PlaintextBytes, item.AddedUnixMs), HorizontalContentAlignment = HorizontalAlignment.Stretch });
                    }
                }

                PopulateBrowser();
                panel.Children.Add(itemList);

                StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
                Button addFileButton = new() { Content = "Add file" };
                Button addFolderButton = new() { Content = "Add folder" };
                Button restoreButton = new() { Content = "Restore selected", IsEnabled = false };
                actions.Children.Add(addFileButton);
                actions.Children.Add(addFolderButton);
                actions.Children.Add(restoreButton);
                panel.Children.Add(actions);
                panel.Children.Add(new TextBlock
                {
                    Text = "Restore never overwrites an existing file or folder. After every selected item is authenticated and restored successfully, Sentinel removes those restored items from the Vault. A failed or partial restore leaves the encrypted Vault items protected.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.78
                });

                ContentDialog manager = new()
                {
                    Title = "Sentinel Vault",
                    Content = new ScrollViewer { Content = panel, MaxHeight = 590 },
                    CloseButtonText = "Close",
                    XamlRoot = rootElement.XamlRoot
                };

                itemList.SelectionChanged += (_, _) =>
                {
                    int count = itemList.SelectedItems.Count;
                    restoreButton.IsEnabled = count > 0;
                    if (count == 1 && itemList.SelectedItems[0] is ListViewItem one && one.Tag is VaultBrowserEntry entry)
                        restoreButton.Content = entry.IsFolder ? "Restore folder" : "Restore file";
                    else
                        restoreButton.Content = count > 1 ? $"Restore selected ({count:N0})" : "Restore selected";
                };
                addFileButton.Click += (_, _) => { requestedAction = "add-file"; manager.Hide(); };
                addFolderButton.Click += (_, _) => { requestedAction = "add-folder"; manager.Hide(); };
                restoreButton.Click += (_, _) =>
                {
                    selectedForRestore = itemList.SelectedItems
                        .OfType<ListViewItem>()
                        .Select(item => item.Tag)
                        .OfType<VaultBrowserEntry>()
                        .ToList();
                    if (selectedForRestore.Count == 0) return;
                    requestedAction = "restore";
                    manager.Hide();
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
            if (requestedAction == "restore" && selectedForRestore.Count > 0)
            {
                string? destinationFolder = await PickVaultExportFolderAsync().ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(destinationFolder))
                    await RestoreVaultSelectionAsync(vaultRoot, selectedForRestore, destinationFolder, rootElement).ConfigureAwait(true);
                continue;
            }
        }
    }

    private async Task RestoreVaultSelectionAsync(string vaultRoot, IReadOnlyCollection<VaultBrowserEntry> browserSelection, string destinationParent, FrameworkElement rootElement)
    {
        PersistedVaultSessionResult open = await new PersistedSentinelVaultService(vaultRoot).OpenCurrentUserAsync().ConfigureAwait(true);
        if (!open.Succeeded || open.Session is null)
        {
            await ShowPrivacyMessageAsync(rootElement, "Vault could not be opened for restore", open.Message + "\n\nStatus: " + open.Code).ConfigureAwait(true);
            return;
        }

        using PersistedVaultSession session = open.Session;
        VaultCommittedItemsResult listed = await session.Items.ListCommittedItemsAsync().ConfigureAwait(true);
        if (!listed.Succeeded)
        {
            await ShowPrivacyMessageAsync(rootElement, "Vault items unavailable", "Sentinel could not authenticate the Vault item index before restore. Nothing was changed.").ConfigureAwait(true);
            return;
        }

        HashSet<Guid> selectedFolderCollections = browserSelection.Where(entry => entry.IsFolder && entry.CollectionId.HasValue).Select(entry => entry.CollectionId!.Value).ToHashSet();
        Dictionary<Guid, VaultMetadataItem> selectedItems = new();
        foreach (VaultBrowserEntry entry in browserSelection.Where(entry => !entry.IsFolder))
        {
            if (entry.CollectionId.HasValue && selectedFolderCollections.Contains(entry.CollectionId.Value)) continue;
            selectedItems[entry.Item.ItemId] = entry.Item;
        }
        foreach (Guid collectionId in selectedFolderCollections)
            foreach (VaultMetadataItem item in listed.Items.Where(item => item.CollectionId == collectionId))
                selectedItems[item.ItemId] = item;

        if (selectedItems.Count == 0)
        {
            await ShowPrivacyMessageAsync(rootElement, "Nothing selected for restore", "Select one or more Vault files or folders, then try again.").ConfigureAwait(true);
            return;
        }

        Dictionary<Guid, string> destinations = new();
        HashSet<string> destinationPaths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> folderRoots = new(StringComparer.OrdinalIgnoreCase);

        foreach (Guid collectionId in selectedFolderCollections)
        {
            VaultMetadataItem[] collection = listed.Items.Where(item => item.CollectionId == collectionId).ToArray();
            if (!TryBuildVaultCollectionRestorePlan(collection, destinationParent, out string destinationRoot, out Dictionary<Guid, string> collectionDestinations, out string planError))
            {
                await ShowPrivacyMessageAsync(rootElement, "Vault folder cannot be restored", planError).ConfigureAwait(true);
                return;
            }
            if (!folderRoots.Add(destinationRoot))
            {
                await ShowPrivacyMessageAsync(rootElement, "Restore destination collision", "Two selected Vault folders resolve to the same destination name. Restore them to separate locations or rename the existing destination first.").ConfigureAwait(true);
                return;
            }
            foreach ((Guid itemId, string destination) in collectionDestinations)
            {
                if (!destinationPaths.Add(destination))
                {
                    await ShowPrivacyMessageAsync(rootElement, "Restore destination collision", "Two selected Vault items resolve to the same destination path. Nothing was restored.").ConfigureAwait(true);
                    return;
                }
                destinations[itemId] = destination;
            }
        }

        foreach (VaultMetadataItem item in selectedItems.Values.Where(item => !item.CollectionId.HasValue || !selectedFolderCollections.Contains(item.CollectionId.Value)))
        {
            string destination = Path.GetFullPath(Path.Combine(destinationParent, GetVaultExportFileName(item)));
            if (File.Exists(destination) || Directory.Exists(destination) || !destinationPaths.Add(destination))
            {
                await ShowPrivacyMessageAsync(rootElement, "Restore destination already exists", $"Sentinel will not overwrite or merge restored content.\n\nDestination:\n{destination}\n\nRename or move the existing item, or choose a different restore location.").ConfigureAwait(true);
                return;
            }
            destinations[item.ItemId] = destination;
        }

        try
        {
            foreach (string directory in destinations.Values.Select(Path.GetDirectoryName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value.Length))
            {
                Directory.CreateDirectory(directory);
                if ((File.GetAttributes(directory) & System.IO.FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A restore directory resolved as a reparse point.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowPrivacyMessageAsync(rootElement, "Vault restore could not start", "Sentinel could not create and verify the destination folders safely. No Vault item was removed or changed.").ConfigureAwait(true);
            return;
        }

        (ContentDialog progressDialog, ProgressBar progressBar, TextBlock progressText) = CreateVaultProgressDialog(rootElement, "Restoring from Sentinel Vault", selectedItems.Count);
        _ = progressDialog.ShowAsync();
        VaultItemExportService exporter = new(vaultRoot, session.Vault, session.Items);
        int restored = 0;
        long restoredBytes = 0;
        try
        {
            foreach (VaultMetadataItem item in selectedItems.Values.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                progressText.Text = $"Authenticating and restoring {restored + 1:N0} of {selectedItems.Count:N0}…\n{GetVaultExportFileName(item)}";
                VaultExportResult result = await exporter.ExportAsync(item.ItemId, destinations[item.ItemId]).ConfigureAwait(true);
                if (!result.Succeeded)
                {
                    progressDialog.Hide();
                    await ShowPrivacyMessageAsync(rootElement, "Vault restore needs attention", $"Sentinel restored {restored:N0} of {selectedItems.Count:N0} selected file(s) before a verified export failed. Existing restored output was left in place rather than risk deleting the wrong filesystem objects. The encrypted Vault items remain protected.\n\nStatus: {result.Code}").ConfigureAwait(true);
                    return;
                }
                restored++;
                restoredBytes += result.PlaintextBytes;
                progressBar.Value = restored;
            }
        }
        finally
        {
            progressDialog.Hide();
        }

        VaultRestoredItemRetirementService retirement = new(vaultRoot, session.Vault);
        VaultRetirementResult retired = await retirement.RetireAsync(selectedItems.Keys.ToArray()).ConfigureAwait(true);
        if (!retired.Succeeded)
        {
            string state = retired.MetadataRetired
                ? "The restored items were removed from the active Vault index, but encrypted ciphertext cleanup could not be fully verified. No plaintext was deleted."
                : "The restored plaintext is verified, but Sentinel kept the encrypted Vault items because retirement could not be verified.";
            await ShowPrivacyMessageAsync(rootElement, "Restore completed with Vault cleanup attention", $"Restored: {restored:N0} file(s) • {FormatBytes(restoredBytes)}\n\n{state}\n\nStatus: {retired.Code}").ConfigureAwait(true);
            return;
        }

        int restoredFolders = selectedFolderCollections.Count;
        await ShowPrivacyMessageAsync(rootElement, "Vault restore complete", $"Sentinel authenticated and restored {restored:N0} file(s){(restoredFolders > 0 ? $" from {restoredFolders:N0} folder(s)" : string.Empty)}, then verified removal of the restored items from the Vault.\n\nRestored: {FormatBytes(restoredBytes)}\n\nThe restored plaintext is now at the destination you selected and no duplicate Vault entries remain.").ConfigureAwait(true);
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
                    relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
                {
                    error = "The authenticated Vault collection contains an invalid relative path. Sentinel refused to create a partial or escaped restore.";
                    return false;
                }
                string destination = Path.GetFullPath(Path.Combine(destinationRoot, relative));
                if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !unique.Add(destination) || File.Exists(destination) || Directory.Exists(destination))
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
        FileOpenPicker picker = new() { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        StorageFile? file = await picker.PickSingleFileAsync();
        return file is null || string.IsNullOrWhiteSpace(file.Path) ? null : file.Path;
    }

    private async Task<string?> PickVaultFolderAsync()
    {
        FolderPicker picker = new() { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder is null || string.IsNullOrWhiteSpace(folder.Path) ? null : folder.Path;
    }

    private async Task<string?> PickVaultExportFolderAsync()
    {
        FolderPicker picker = new() { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
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
