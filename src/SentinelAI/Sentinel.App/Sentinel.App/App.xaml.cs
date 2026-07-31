using Microsoft.UI.Xaml;
using Sentinel.App.Services;
using System;
using System.Diagnostics;

namespace Sentinel.App
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private readonly DiagnosticLogService _diagnosticLog = new();
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            InitializeComponent();
            UnhandledException += App_UnhandledException;
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            Stopwatch startupTimer = Stopwatch.StartNew();
            _ = _diagnosticLog.InformationAsync("ApplicationLaunch", "Sentinel AI launch started.");

            try
            {
                // Keep disk diagnostics and development-only verification off the critical
                // path that creates and activates the first visible window.
                _window = new MainWindow();
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
                _ = _diagnosticLog.ErrorAsync(
                    "ApplicationLaunchFailure",
                    $"Sentinel AI could not activate the main window after {startupTimer.ElapsedMilliseconds} ms.",
                    ex);
                throw;
            }
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
