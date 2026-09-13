using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;

namespace Sentinel.App
{
    public partial class App
    {
        private const string ExplorerRestartPromptVersionKey = "ExplorerRestartPromptedPackageVersion";

        private async Task PromptForExplorerRestartAfterInstallAsync(Window ownerWindow)
        {
            try
            {
                PackageVersion packageVersion = Package.Current.Id.Version;
                string version = $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";
                ApplicationDataContainer settings = ApplicationData.Current.LocalSettings;
                string? lastPromptedVersion = settings.Values[ExplorerRestartPromptVersionKey] as string;
                if (string.Equals(lastPromptedVersion, version, StringComparison.Ordinal))
                    return;

                if (ownerWindow.Content is not FrameworkElement root)
                    return;

                ContentDialog dialog = new()
                {
                    XamlRoot = root.XamlRoot,
                    Title = "Restart Windows to finish Sentinel AI setup",
                    Content = "Sentinel AI is installed. Restart Windows before testing File Explorer right-click commands so the Sentinel Explorer extension can finish registering. You can restart now or restart later.",
                    PrimaryButtonText = "Restart now",
                    CloseButtonText = "Restart later",
                    DefaultButton = ContentDialogButton.Primary
                };

                ContentDialogResult result = await dialog.ShowAsync();
                settings.Values[ExplorerRestartPromptVersionKey] = version;

                if (result == ContentDialogResult.Primary)
                {
                    _ = _diagnosticLog.InformationAsync("ExplorerRestartPrompt", $"User chose to restart Windows after installing Sentinel AI {version}.");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown.exe",
                        Arguments = "/r /t 5 /c \"Sentinel AI setup is complete. Restarting Windows to finish File Explorer integration.\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                else
                {
                    _ = _diagnosticLog.WarningAsync("ExplorerRestartPrompt", $"User postponed the Windows restart after installing Sentinel AI {version}; Explorer context-menu testing is not yet valid.");
                }
            }
            catch (InvalidOperationException)
            {
                // Unpackaged/debug launches may not have package identity; no install restart prompt is needed.
            }
            catch (Exception ex)
            {
                _ = _diagnosticLog.WarningAsync("ExplorerRestartPrompt", "Sentinel AI could not display the post-install restart prompt.", ex);
            }
        }
    }
}
