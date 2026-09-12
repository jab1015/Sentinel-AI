using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sentinel.App.Services;
using System;
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

        string[] names = request.Paths
            .Take(5)
            .Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        string selection = names.Length == 0 ? "the selected filesystem item" : string.Join(Environment.NewLine, names.Select(name => "• " + name));
        if (request.Paths.Count > names.Length)
            selection += Environment.NewLine + $"• and {request.Paths.Count - names.Length} more";

        ContentDialog dialog = new()
        {
            Title = "Inspect with Sentinel AI",
            Content = $"Sentinel received {request.Paths.Count} selected item(s) from File Explorer and reopened the filesystem paths for validation. No file was changed.\n\n{selection}",
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = rootElement.XamlRoot
        };

        await dialog.ShowAsync();
    }
}
