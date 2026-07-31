using Microsoft.UI.Xaml;
using Sentinel.App.Services;
using System;

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
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            await _diagnosticLog.InformationAsync("ApplicationLaunch", "Sentinel AI launch started.");

            try
            {
                _window = new MainWindow();
                _window.Activate();
                await _diagnosticLog.InformationAsync("ApplicationLaunch", "Sentinel AI main window activated.");
            }
            catch (Exception ex)
            {
                await _diagnosticLog.ErrorAsync("ApplicationLaunchFailure", "Sentinel AI could not activate the main window.", ex);
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
