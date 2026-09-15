using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Sentinel.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.App;

public sealed partial class MainWindow
{
    internal void HandleExplorerInspectionRequest(ExplorerInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        DispatcherQueue.TryEnqueue(() => _ = ShowExplorerInspectionRequestAsync(request));
    }

    private async Task ShowExplorerInspectionRequestAsync(ExplorerInspectionRequest request)
    {
        // File Explorer actions run in a dedicated, normally sized native WinUI host. A previous
        // 1x1 host could leave only a tiny title bar visible on real Windows systems and constrain
        // ContentDialog layout until the user manually maximized it. Keep the dashboard hidden,
        // but give the action UI a real XamlRoot with enough space to lay itself out correctly.
        Window dialogHost = new();
        Grid rootElement = new()
        {
            MinWidth = 700,
            MinHeight = 560,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 7, 20, 42))
        };
        rootElement.Resources["ContentDialogSmokeLayerBackground"] =
            new SolidColorBrush(Windows.UI.Color.FromArgb(170, 7, 20, 42));
        dialogHost.Content = rootElement;
        dialogHost.AppWindow.Title = "Sentinel AI";
        dialogHost.AppWindow.Resize(new Windows.Graphics.SizeInt32(740, 640));
        dialogHost.Activate();
        await WaitForXamlRootAsync(rootElement).ConfigureAwait(true);

        try
        {
            await ShowExplorerInspectionRequestCoreAsync(request, rootElement).ConfigureAwait(true);
        }
        finally
        {
            dialogHost.Close();
        }
    }

    private async Task ShowExplorerInspectionRequestCoreAsync(
        ExplorerInspectionRequest request,
        FrameworkElement rootElement)
    {
        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        List<string> reopenedPaths = new(request.Paths.Count);
        foreach (string suppliedPath in request.Paths)
        {
            if (!ExplorerSelectionObjectValidator.TryReopenAndResolve(suppliedPath, out string resolvedPath))
            {
                await new ContentDialog
                {
                    Title = "Explorer action could not start",
                    Content = "Sentinel could not reopen every selected filesystem object safely. The selection may have changed, become unavailable, or no longer resolve to a normal filesystem object. No file was changed.",
                    CloseButtonText = "Close",
                    XamlRoot = rootElement.XamlRoot
                }.ShowAsync();
                return;
            }

            if (unique.Add(resolvedPath)) reopenedPaths.Add(resolvedPath);
        }

        if (reopenedPaths.Count == 0)
        {
            await new ContentDialog
            {
                Title = "Explorer action could not start",
                Content = "Sentinel could not resolve a usable filesystem object from the Explorer selection. No file was changed.",
                CloseButtonText = "Close",
                XamlRoot = rootElement.XamlRoot
            }.ShowAsync();
            return;
        }

        if (request.Action != ExplorerRequestedAction.Inspect)
        {
            if (reopenedPaths.Count != 1)
            {
                await new ContentDialog
                {
                    Title = "One selection required",
                    Content = "Sentinel Explorer privacy actions accept one selected file or folder at a time. No file was changed.",
                    CloseButtonText = "Close",
                    XamlRoot = rootElement.XamlRoot
                }.ShowAsync();
                return;
            }

            if (request.Action == ExplorerRequestedAction.AddToVault)
            {
                await AddExplorerSelectionToVaultAsync(reopenedPaths[0], rootElement).ConfigureAwait(true);
                return;
            }

            if (Directory.Exists(reopenedPaths[0]))
            {
                await new ContentDialog
                {
                    Title = "A file is required",
                    Content = "This Sentinel privacy action currently accepts a normal file. Use Add to Vault when you want to protect an entire folder.",
                    CloseButtonText = "Close",
                    XamlRoot = rootElement.XamlRoot
                }.ShowAsync();
                return;
            }

            await ShowExplorerPremiumPrivacyRequestAsync(request.Action, reopenedPaths[0], rootElement).ConfigureAwait(true);
            return;
        }

        if (reopenedPaths.Count == 1 &&
            File.Exists(reopenedPaths[0]) &&
            reopenedPaths[0].EndsWith(".sentinel.senc", StringComparison.OrdinalIgnoreCase))
        {
            string encryptedPath = reopenedPaths[0];
            ContentDialog encryptedDialog = new()
            {
                Title = "Sentinel encrypted file",
                Content = $"Sentinel recognized this encrypted container:\n\n{encryptedPath}\n\nYou can restore it to plaintext or inspect the encrypted container without changing it.",
                PrimaryButtonText = "Decrypt / Restore",
                SecondaryButtonText = "Inspect",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = rootElement.XamlRoot
            };

            ContentDialogResult encryptedChoice = await encryptedDialog.ShowAsync();
            if (encryptedChoice == ContentDialogResult.Primary)
            {
                await DecryptExplorerFileAsync(encryptedPath, rootElement).ConfigureAwait(true);
                return;
            }
            if (encryptedChoice != ContentDialogResult.Secondary)
                return;
        }

        string[] names = reopenedPaths
            .Take(5)
            .Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        string selection = names.Length == 0 ? "the selected filesystem item" : string.Join(Environment.NewLine, names.Select(name => "• " + name));
        if (reopenedPaths.Count > names.Length)
            selection += Environment.NewLine + $"• and {reopenedPaths.Count - names.Length} more";

        ContentDialog dialog = new()
        {
            Title = "Inspect with Sentinel AI",
            Content = $"Selected:\n{selection}\n\nWhat Sentinel will check:\n• Reopen each selected path and make sure it still resolves to a normal filesystem object.\n• For a file, read the complete file and calculate a SHA-256 fingerprint without changing it.\n• For a folder, read its metadata and enumerate the immediate items that Windows allows Sentinel to see.\n\nWhy this check exists:\nIt can reveal path, access, read, or filesystem-object problems before Sentinel relies on the item for another action.\n\nWhat this check does NOT do:\nThis is not a malware or antivirus scan and it does not declare a file safe from malicious code.",
            PrimaryButtonText = "Inspect",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = rootElement.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        string result = await BuildExplorerInspectionSummaryAsync(reopenedPaths).ConfigureAwait(true);
        await new ContentDialog
        {
            Title = "Sentinel inspection result",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = result,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                },
                MaxHeight = 500
            },
            CloseButtonText = "OK",
            XamlRoot = rootElement.XamlRoot
        }.ShowAsync();
    }

    private static async Task<string> BuildExplorerInspectionSummaryAsync(IReadOnlyList<string> paths)
    {
        StringBuilder details = new();
        bool incomplete = false;
        int shown = 0;

        foreach (string path in paths)
        {
            if (shown >= 5)
            {
                details.AppendLine($"…and {paths.Count - shown} more item(s).");
                break;
            }

            if (Directory.Exists(path))
            {
                DirectoryInfo directory = new(path);
                details.AppendLine($"Folder: {directory.Name}");
                details.AppendLine($"Location: {directory.FullName}");
                details.AppendLine("Type: Folder");
                details.AppendLine($"Last modified: {directory.LastWriteTime:MMM d, yyyy h:mm:ss tt}");
                try
                {
                    int objectCount = Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly).Take(1001).Count();
                    details.AppendLine(objectCount > 1000 ? "Immediate contents: More than 1,000 objects" : $"Immediate contents: {objectCount:N0} object(s)");
                    details.AppendLine("What Sentinel checked: Sentinel reopened the folder, read its basic filesystem metadata, and enumerated the immediate contents that Windows allowed it to see.");
                    details.AppendLine("Finding: No path, access, or immediate-enumeration problem was encountered during this read-only check.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    incomplete = true;
                    details.AppendLine("What Sentinel checked: Sentinel tried to reopen the folder and enumerate its immediate contents.");
                    details.AppendLine("Finding: Windows would not let Sentinel complete the folder check.");
                    details.AppendLine("Recommended action: Check the folder's availability and permissions, then inspect it again. Treat the result as unverified until the check completes.");
                }
                details.AppendLine("Not checked: Files inside this folder were not recursively scanned for malware by this inspection command.");
                details.AppendLine();
                shown++;
                continue;
            }

            FileInfo file = new(path);
            string sha256;
            try
            {
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using SHA256 hash = SHA256.Create();
                byte[] digest = await hash.ComputeHashAsync(stream).ConfigureAwait(false);
                sha256 = Convert.ToHexString(digest);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                incomplete = true;
                details.AppendLine($"File: {file.Name}");
                details.AppendLine($"Location: {file.FullName}");
                details.AppendLine("Type: File");
                details.AppendLine("What Sentinel checked: Sentinel tried to reopen the selected file and read it from beginning to end without changing it.");
                details.AppendLine("Finding: The read-only file check could not be completed.");
                details.AppendLine("Recommended action: Treat this item as unverified. Check that the file is still available and readable, then inspect it again.");
                details.AppendLine("Not checked: Sentinel did not make a malware or antivirus determination for this file.");
                details.AppendLine();
                shown++;
                continue;
            }

            details.AppendLine($"File: {file.Name}");
            details.AppendLine($"Location: {file.FullName}");
            details.AppendLine("Type: File");
            details.AppendLine($"Size: {file.Length:N0} bytes");
            details.AppendLine($"Last modified: {file.LastWriteTime:MMM d, yyyy h:mm:ss tt}");
            details.AppendLine("What Sentinel checked: Sentinel reopened the selected file, read every byte successfully, and calculated a SHA-256 fingerprint without modifying the file.");
            details.AppendLine("Finding: No path, access, or read error was encountered during this filesystem check.");
            details.AppendLine($"Technical fingerprint (SHA-256): {sha256}");
            details.AppendLine("Not checked: This inspection did not scan the file for malware and does not mean the file is malware-free.");
            details.AppendLine();
            shown++;
        }

        StringBuilder summary = new();
        summary.AppendLine("WHAT SENTINEL CHECKED");
        summary.AppendLine(paths.Count == 1
            ? "Sentinel performed a read-only filesystem check on the selected item."
            : $"Sentinel performed read-only filesystem checks on {paths.Count:N0} selected items.");
        summary.AppendLine();
        summary.AppendLine("WHAT SENTINEL FOUND");
        if (incomplete)
        {
            summary.AppendLine("CHECK INCOMPLETE — COULD NOT FULLY VERIFY");
            summary.AppendLine("At least one selected item could not be completely reopened, read, or enumerated. Sentinel is not making a safety determination for that item.");
            summary.AppendLine("Recommended action: Review the item details below and repeat the inspection after correcting any access or availability problem.");
        }
        else
        {
            summary.AppendLine("READ-ONLY FILESYSTEM CHECK COMPLETED");
            summary.AppendLine("Sentinel did not encounter a path, access, read, or immediate folder-enumeration error in the checks it actually performed.");
            summary.AppendLine("No action is required for those filesystem checks.");
        }

        summary.AppendLine();
        summary.AppendLine("IMPORTANT LIMIT");
        summary.AppendLine("This inspection is not a malware or antivirus scan. A successful result does not declare the selected file or folder safe from malicious code.");
        summary.AppendLine($"Items inspected: {paths.Count:N0}");
        summary.AppendLine("Changes made: None");
        summary.AppendLine();
        summary.AppendLine("ITEM DETAILS");
        summary.Append(details);
        summary.Append("Sentinel did not modify the selection.");
        return summary.ToString();
    }
}