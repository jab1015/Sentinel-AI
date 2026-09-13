using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        AppWindow.Show();
        Activate();

        FrameworkElement rootElement = (FrameworkElement)Content;
        if (rootElement.XamlRoot is null)
        {
            TaskCompletionSource<bool> loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler? handler = null;
            handler = (_, _) =>
            {
                rootElement.Loaded -= handler;
                loaded.TrySetResult(true);
            };
            rootElement.Loaded += handler;
            await loaded.Task.ConfigureAwait(true);
        }

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
            if (reopenedPaths.Count != 1 || Directory.Exists(reopenedPaths[0]))
            {
                await new ContentDialog
                {
                    Title = "One file required",
                    Content = "Premium Privacy Explorer actions currently accept exactly one normal file at a time. No file was changed.",
                    CloseButtonText = "Close",
                    XamlRoot = rootElement.XamlRoot
                }.ShowAsync();
                return;
            }

            await ShowExplorerPremiumPrivacyRequestAsync(request.Action, reopenedPaths[0], rootElement).ConfigureAwait(true);
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
            Content = $"Sentinel received {request.Paths.Count} selected item(s) from File Explorer, reopened each object through Windows, and resolved the handle-backed filesystem path before inspection. No file will be changed.\n\n{selection}",
            PrimaryButtonText = "Continue",
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
        StringBuilder summary = new();
        summary.AppendLine($"Items inspected: {paths.Count}");
        summary.AppendLine("No file was changed.");
        summary.AppendLine();

        int shown = 0;
        foreach (string path in paths)
        {
            if (shown >= 5)
            {
                summary.AppendLine($"…and {paths.Count - shown} more item(s)."
                );
                break;
            }

            if (Directory.Exists(path))
            {
                DirectoryInfo directory = new(path);
                summary.AppendLine($"Folder: {directory.Name}");
                summary.AppendLine($"Path: {directory.FullName}");
                summary.AppendLine($"Last modified: {directory.LastWriteTime:MMM d, yyyy h:mm:ss tt}");
                summary.AppendLine("Result: Filesystem object reopened and verified as an accessible directory.");
                summary.AppendLine();
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
                sha256 = "Unavailable (Windows could not complete the integrity hash).";
            }

            summary.AppendLine($"File: {file.Name}");
            summary.AppendLine($"Path: {file.FullName}");
            summary.AppendLine($"Size: {FormatBytes(file.Length)}");
            summary.AppendLine($"Last modified: {file.LastWriteTime:MMM d, yyyy h:mm:ss tt}");
            summary.AppendLine($"SHA-256: {sha256}");
            summary.AppendLine("Result: File identity and local integrity evidence collected. This result is not, by itself, a malware-clean verdict.");
            summary.AppendLine();
            shown++;
        }

        return summary.ToString().TrimEnd();
    }
}
