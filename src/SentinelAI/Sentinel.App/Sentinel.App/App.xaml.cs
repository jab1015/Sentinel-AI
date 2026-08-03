using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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
            _ = _diagnosticLog.InformationAsync("ApplicationLaunch", "Sentinel AI launch started.");

            try
            {
                WindowsStartupRegistrationService.StartupRegistrationResult startup =
                    _startupRegistrationService.EnsureRegisteredAndVerify();

                _ = startup.Registered
                    ? _diagnosticLog.InformationAsync("WindowsStartup", startup.Summary)
                    : _diagnosticLog.WarningAsync("WindowsStartup", startup.Summary);

                _window = new MainWindow();
                _window.AppWindow.Closing += MainAppWindow_Closing;
                _systemTrayService = new SystemTrayService(ShowMainWindow, ExitApplication);
                _window.Activate();

                startupTimer.Stop();
                _ = _diagnosticLog.InformationAsync(
                    "StartupPerformance",
                    $"Main window activated in {startupTimer.ElapsedMilliseconds} ms.");

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
                    $"Sentinel AI could not activate the main window after {startupTimer.ElapsedMilliseconds} ms.",
                    ex);
                throw;
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
