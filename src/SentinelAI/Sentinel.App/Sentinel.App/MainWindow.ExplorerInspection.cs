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
        // Explorer commands intentionally use a compact, bright host instead of surfacing the
        // full Sentinel dashboard. The host exists only to provide a XamlRoot for the dialog.
        Window dialogHost = new();
        Grid rootElement = new()
        {
            MinWidth = 520,
            MinHeight = 300,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
        };
        rootElement.Resources["ContentDialogSmokeLayerBackground"] =
            new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        dialogHost.Content = rootElement;
        dialogHost.AppWindow.Title = "Sentinel AI";
        dialogHost.AppWindow.Resize(new Windows.Graphics.SizeInt32(620, 440));
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
            Content = $"Sentinel will perform a read-only filesystem inspection of {request.Paths.Count} selected item(s). This does not replace a malware scan.\n\n{selection}",
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
            Content = result,
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
                    details.AppendLine(objectCount > 1000 ? "Objects: More than 1,000 immediate objects" : $"Objects: {objectCount:N0} immediate object(s)");
                    details.AppendLine("Assessment: The folder was reopened and its immediate contents could be enumerated.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    incomplete = true;
                    details.AppendLine("Assessment: Sentinel could not enumerate this folder completely.");
                    details.AppendLine("Recommended action: Check access to the folder and inspect it again.");
                }
                details.AppendLine("Scope: This is a filesystem accessibility check, not a malware scan of files inside the folder.");
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
                details.AppendLine("Assessment: Sentinel could not complete the read-only content check.");
                details.AppendLine("Recommended action: Treat this item as unverified and inspect it again after checking access.");
                details.AppendLine();
                shown++;
                continue;
            }

            details.AppendLine($"File: {file.Name}");
            details.AppendLine($"Location: {file.FullName}");
            details.AppendLine("Type: File");
            details.AppendLine($"Size: {file.Length:N0} bytes");
            details.AppendLine($"Last modified: {file.LastWriteTime:MMM d, yyyy h:mm:ss tt}");
            details.AppendLine($"SHA-256: {sha256}");
            details.AppendLine("Assessment: The file was reopened, read successfully, and hashed without modifying it.");
            details.AppendLine("Scope: This integrity check does not establish that the file is malware-free.");
            details.AppendLine();
            shown++;
        }

        StringBuilder summary = new();
        if (incomplete)
        {
            summary.AppendLine("UNKNOWN / COULD NOT FULLY VERIFY");
            summary.AppendLine("One or more read-only filesystem checks could not be completed. Sentinel is not making a safety determination for this selection.");
        }
        else
        {
            summary.AppendLine("NO FILESYSTEM ISSUES FOUND");
            summary.AppendLine("The requested read-only filesystem checks completed successfully. This result is not a malware verdict.");
        }

        summary.AppendLine($"Items inspected: {paths.Count}");
        summary.AppendLine("No file was changed.");
        summary.AppendLine();
        summary.Append(details);
        summary.Append("Sentinel did not modify the selection.");
        return summary.ToString();
    }
}
