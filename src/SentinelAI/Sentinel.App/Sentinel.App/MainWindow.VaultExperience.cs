using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sentinel.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace Sentinel.App;

public sealed partial class MainWindow
{
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
        if (isDirectory)
        {
            try
            {
                if ((File.GetAttributes(path) & System.IO.FileAttributes.ReparsePoint) != 0)
                {
                    await ShowPrivacyMessageAsync(rootElement, "Folder cannot be added", "Sentinel will not import a reparse-point folder into the Vault because its contents can resolve outside the selected folder.");
                    return;
                }
                files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToArray();
                if (files.Any(file => (File.GetAttributes(file) & System.IO.FileAttributes.ReparsePoint) != 0))
                {
                    await ShowPrivacyMessageAsync(rootElement, "Folder cannot be added", "The folder contains a reparse-point file. Sentinel left the folder unchanged rather than importing an ambiguous filesystem object.");
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await ShowPrivacyMessageAsync(rootElement, "Folder could not be read", "Sentinel could not enumerate the entire folder safely. Nothing was moved into the Vault.");
                return;
            }
        }
        else
        {
            files = new[] { path };
        }

        if (files.Length == 0)
        {
            await ShowPrivacyMessageAsync(rootElement, "Folder is empty", "There are no files to protect in this folder.");
            return;
        }

        long totalBytes = 0;
        try { totalBytes = files.Sum(file => new FileInfo(file).Length); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        ContentDialog confirmation = new()
        {
            Title = isDirectory ? "Move folder to Sentinel Vault" : "Move file to Sentinel Vault",
            Content = isDirectory
                ? $"Folder:\n{path}\n\nFiles: {files.Length:N0}\nSize: {FormatBytes(totalBytes)}\n\nSentinel will encrypt and verify each file inside its private Vault. Only after each Vault item is verified will Sentinel retire that exact readable source file. When every file has moved successfully, the now-empty source folder will be removed. If any item cannot be verified or safely retired, Sentinel stops and reports exactly what remains."
                : $"File:\n{path}\n\nSize: {FormatBytes(totalBytes)}\n\nSentinel will encrypt and verify the file inside its private Vault. Only after the Vault item is verified will Sentinel retire the exact readable source file. If either step cannot be proven safe, Sentinel will report the problem instead of silently claiming the move succeeded.",
            PrimaryButtonText = "Move to Vault",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = rootElement.XamlRoot
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

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

        int moved = 0;
        long movedBytes = 0;
        string? failure = null;
        using (PersistedVaultSession session = open.Session)
        {
            foreach (string source in files)
            {
                SecureDeleteTargetValidationResult identity = SecureDeleteTargetValidator.Validate(source);
                if (!identity.Succeeded)
                {
                    failure = $"Sentinel could not bind this source to an exact filesystem identity before the move:\n{source}\n\n{identity.Message}";
                    break;
                }

                VaultAddItemResult added = await session.Items.AddFileAsync(source).ConfigureAwait(true);
                if (!added.Succeeded)
                {
                    failure = $"Sentinel could not commit a verified Vault item for:\n{source}\n\nStatus: {added.Code}";
                    break;
                }

                if (!TryRetireVaultSource(identity.Target, out string retirementStatus))
                {
                    failure = $"The file is safely stored and verified in the Vault, but Sentinel could not prove safe retirement of the readable source:\n{source}\n\n{retirementStatus}\n\nThe source was kept for safety.";
                    break;
                }

                moved++;
                movedBytes += added.PlaintextBytes;
            }
        }

        if (failure is null && isDirectory)
        {
            try
            {
                foreach (string directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                             .OrderByDescending(value => value.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
                }
                if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure = "All files were moved into the Vault, but Sentinel could not remove one or more empty source folders. No readable source file was intentionally left behind.";
            }
        }

        if (failure is null)
        {
            await ShowPrivacyMessageAsync(rootElement, "Moved to Sentinel Vault",
                $"Sentinel encrypted, authenticated, and committed {moved:N0} file(s), then verified retirement of each readable source file.\n\nProtected: {FormatBytes(movedBytes)}\n\nOpen Vault from the Sentinel dashboard to view protected items.").ConfigureAwait(true);
        }
        else
        {
            await ShowPrivacyMessageAsync(rootElement, "Vault move needs attention",
                $"Files fully moved before the stop: {moved:N0} of {files.Length:N0}\nProtected and source-retired: {FormatBytes(movedBytes)}\n\n{failure}").ConfigureAwait(true);
        }
    }

    private static bool TryRetireVaultSource(SecureDeleteValidatedTarget target, out string status)
    {
        SecureDeleteCoordinator coordinator = new();
        SecureDeletePreparationResult prepared = coordinator.Prepare(target);
        if (!prepared.Succeeded || prepared.Authorization is null)
        {
            status = prepared.Message;
            return false;
        }

        string journalRoot = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Privacy", "SecureDeleteJournal");
        SecureDeleteOperationJournal journal = new(journalRoot);
        SecureDeleteExactObjectExecutor executor = new(coordinator, journal);
        SecureDeleteExecutionResult result = executor.Execute(prepared.Authorization);
        if (result.Succeeded && result.PrimaryRemoval == SecureDeletePrimaryRemovalStatus.Verified)
        {
            status = "Verified removed";
            return true;
        }

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
        TextBox keyBox = new()
        {
            Header = "Vault recovery key",
            Text = recoveryText,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            IsSpellCheckEnabled = false
        };
        StackPanel panel = new() { Spacing = 10, MinWidth = 430 };
        panel.Children.Add(new TextBlock
        {
            Text = "Save this recovery key somewhere separate from this PC. It is the independent recovery path to your Vault. Sentinel does not upload or retain the plaintext recovery key.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(keyBox);
        panel.Children.Add(new TextBlock { Text = "Click the key box and press Ctrl+A, then Ctrl+C to copy it.", TextWrapping = TextWrapping.Wrap });

        ContentDialog dialog = new()
        {
            Title = "Create Sentinel Vault",
            Content = panel,
            PrimaryButtonText = "I saved the key",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = rootElement.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return PersistedVaultSessionResult.Fail("Canceled", "Vault creation was canceled.");

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
        PersistedSentinelVaultService persistence = new(vaultRoot);
        if (!persistence.Exists)
        {
            await ShowPrivacyMessageAsync(rootElement, "Sentinel Vault", "Your Vault has not been created yet. Right-click a file or folder in File Explorer and choose Sentinel AI → Vault to create it and move protected content inside.");
            return;
        }

        PersistedVaultSessionResult open = await persistence.OpenCurrentUserAsync().ConfigureAwait(true);
        if (!open.Succeeded || open.Session is null)
        {
            await ShowPrivacyMessageAsync(rootElement, "Vault could not be opened", open.Message + "\n\nStatus: " + open.Code).ConfigureAwait(true);
            return;
        }

        using PersistedVaultSession session = open.Session;
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
        StackPanel panel = new() { Spacing = 10, MinWidth = 460 };
        panel.Children.Add(new TextBlock
        {
            Text = items.Items.Count == 0
                ? "Your Vault is empty."
                : $"{items.Items.Count:N0} protected item(s) • {FormatBytes(total)}",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Vault contents are encrypted inside Sentinel's private app storage. Source files are removed only after the corresponding Vault item is authenticated and committed.",
            TextWrapping = TextWrapping.Wrap
        });

        foreach (VaultMetadataItem item in items.Items.Take(20))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"🔒 Protected item {item.ItemId.ToString("N")[..8]}  •  {FormatBytes(item.PlaintextBytes)}",
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (items.Items.Count > 20)
            panel.Children.Add(new TextBlock { Text = $"…and {items.Items.Count - 20:N0} more protected item(s)." });

        await new ContentDialog
        {
            Title = "Sentinel Vault",
            Content = new ScrollViewer { Content = panel, MaxHeight = 480 },
            CloseButtonText = "Close",
            XamlRoot = rootElement.XamlRoot
        }.ShowAsync();
    }
}
