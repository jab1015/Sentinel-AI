using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sentinel.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            Content = $"Sentinel received {request.Paths.Count} selected item(s) from File Explorer, reopened each object through Windows, and resolved the handle-backed filesystem path before inspection. No file was changed.\n\n{selection}",
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = rootElement.XamlRoot
        };

        await dialog.ShowAsync();
    }
}
