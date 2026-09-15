using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Storage;

namespace Sentinel.App
{
    public partial class App
    {
        private const string ExplorerRestartPromptVersionKey = "ExplorerRestartPromptedPackageVersion";
        private const uint MbYesNo = 0x00000004;
        private const uint MbIconInformation = 0x00000040;
        private const uint MbDefaultButton1 = 0x00000000;
        private const int IdYes = 6;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

        internal void PromptForExplorerRestartAfterInstall()
        {
            try
            {
                PackageVersion packageVersion = Package.Current.Id.Version;
                string version = $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";
                ApplicationDataContainer settings = ApplicationData.Current.LocalSettings;
                string? lastPromptedVersion = settings.Values[ExplorerRestartPromptVersionKey] as string;
                if (string.Equals(lastPromptedVersion, version, StringComparison.Ordinal))
                    return;

                int result = MessageBoxW(
                    IntPtr.Zero,
                    "Sentinel AI is installed. Windows must restart before the Sentinel File Explorer right-click commands are considered ready.\n\nRestart Windows now?\n\nChoose Yes to restart now or No to restart later.",
                    "Restart Windows to finish Sentinel AI setup",
                    MbYesNo | MbIconInformation | MbDefaultButton1);

                settings.Values[ExplorerRestartPromptVersionKey] = version;

                if (result == IdYes)
                {
                    _ = _diagnosticLog.InformationAsync("ExplorerRestartPrompt", $"User chose to restart Windows after installing Sentinel AI {version}.");
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown.exe",
                            Arguments = "/r /t 5 /c \"Sentinel AI setup is complete. Restarting Windows to finish File Explorer integration.\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                    }
                    catch (Exception ex)
                    {
                        _ = _diagnosticLog.ErrorAsync("ExplorerRestartPrompt", "Windows could not be restarted automatically. The user can restart manually before testing File Explorer integration.", ex);
                    }
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
                // The post-install prompt is never allowed to prevent Sentinel from starting.
                _ = _diagnosticLog.ErrorAsync("ExplorerRestartPrompt", "Sentinel AI could not display the post-install restart prompt. Startup will continue normally.", ex);
            }
        }
    }
}
