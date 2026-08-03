using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Sentinel.App.Services;
using System;
using System.Diagnostics;

namespace Sentinel.App
{
    public partial class App : Application
    {
        private readonly DiagnosticLogService _diagnosticLog = new();
        private readonly WindowsStartupRegistrationService _startupRegistrationService = new();
        private Window? _window;
        private OptionsWindow? _optionsWindow;
        private SystemTrayService? _systemTrayService;
        private bool _isExplicitExit;

        public App()
        {
            InitializeComponent();
            UnhandledException += App_UnhandledException;
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            Stopwatch startupTimer = Stopwatch.StartNew();
            bool launchedByWindowsStartup = IsWindowsStartupLaunch();
            _ = _diagnosticLog.InformationAsync(
                "ApplicationLaunch",
                launchedByWindowsStartup
                    ? "Sentinel AI Windows startup launch started."
                    : "Sentinel AI interactive launch started.");

            try
            {
                WindowsStartupRegistrationService.StartupRegistrationResult startup =
                    _startupRegistrationService.EnsureRegisteredAndVerify();

                _ = startup.Registered
                    ? _diagnosticLog.InformationAsync("WindowsStartup", startup.Summary)
                    : _diagnosticLog.WarningAsync("WindowsStartup", startup.Summary);

                _window = new MainWindow();
                _window.AppWindow.Closing += MainAppWindow_Closing;
                _systemTrayService = new SystemTrayService(ShowMainWindow, ShowOptionsWindow, ExitApplication);

                if (launchedByWindowsStartup)
                {
                    _window.AppWindow.Hide();
                    _ = _diagnosticLog.InformationAsync(
                        "WindowsStartup",
                        "Sentinel AI started with Windows and is monitoring from the system tray.");
                }
                else
                {
                    _window.Activate();
                }

                startupTimer.Stop();
                _ = _diagnosticLog.InformationAsync(
                    "StartupPerformance",
                    launchedByWindowsStartup
                        ? $"Background startup completed in {startupTimer.ElapsedMilliseconds} ms."
                        : $"Main window activated in {startupTimer.ElapsedMilliseconds} ms.");

#if DEBUG
                DevelopmentRegressionChecks.Run();
                _ = _diagnosticLog.InformationAsync("RegressionChecks", "Development safety regression checks passed.");
#endif
            }
            catch (Exception ex)
            {
                startupTimer.Stop();
                _systemTrayService?.Dispose();
                _systemTrayService = null;
                _ = _diagnosticLog.ErrorAsync(
                    "ApplicationLaunchFailure",
                    $"Sentinel AI could not complete startup after {startupTimer.ElapsedMilliseconds} ms.",
                    ex);
                throw;
            }
        }

        private static bool IsWindowsStartupLaunch()
        {
            try
            {
                AppActivationArguments? activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                return activation is not null && activation.Kind == ExtendedActivationKind.StartupTask;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void MainAppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isExplicitExit) return;

            args.Cancel = true;
            sender.Hide();
            _ = _diagnosticLog.InformationAsync(
                "SystemTray",
                "Main window hidden. Sentinel AI continues monitoring in the system tray.");
        }

        private void ShowMainWindow()
        {
            Window? window = _window;
            if (window is null) return;

            window.DispatcherQueue.TryEnqueue(() =>
            {
                window.AppWindow.Show();
                window.Activate();
            });
        }

        private void ShowOptionsWindow()
        {
            Window? window = _window;
            if (window is null) return;

            window.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (_optionsWindow is null)
                    {
                        _optionsWindow = new OptionsWindow();
                        _optionsWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(720, 440));
                        _optionsWindow.AppWindow.Closing += (_, _) => _optionsWindow = null;
                    }

                    _optionsWindow.AppWindow.Show();
                    _optionsWindow.Activate();
                    _ = _diagnosticLog.InformationAsync("Options", "Sentinel AI Options opened from the system tray.");
                }
                catch (Exception ex)
                {
                    _ = _diagnosticLog.ErrorAsync("OptionsOpenFailure", "Sentinel AI could not open Options.", ex);
                    window.AppWindow.Show();
                    window.Activate();
                }
            });
        }

        private void ExitApplication()
        {
            Window? window = _window;
            if (window is null)
            {
                _systemTrayService?.Dispose();
                _systemTrayService = null;
                Exit();
                return;
            }

            window.DispatcherQueue.TryEnqueue(() =>
            {
                _isExplicitExit = true;
                _optionsWindow?.Close();
                _optionsWindow = null;
                _systemTrayService?.Dispose();
                _systemTrayService = null;
                window.Close();
                Exit();
            });
        }

        private async void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            await _diagnosticLog.ErrorAsync(
                "UnhandledException",
                "An unhandled application exception reached the WinUI application boundary.",
                e.Exception);
        }
    }
}
