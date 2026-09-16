using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Sentinel.App.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace Sentinel.App
{
    public partial class App : Application
    {
        private const string MainInstanceKey = "SentinelAI.Main";
        private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

        private readonly DiagnosticLogService _diagnosticLog = new();
        private readonly WindowsStartupRegistrationService _startupRegistrationService = new();
        private readonly ExplorerHandoffService _explorerHandoffService;
        private AppInstance? _primaryInstance;
        private Window? _window;
        private OptionsWindow? _optionsWindow;
        private SystemTrayService? _systemTrayService;
        private ExplorerInspectionRequest? _pendingExplorerInspection;
        private bool _isExplicitExit;
        private bool _pendingInteractiveActivation;

        public App()
        {
            EnsurePerMonitorDpiAwareness();
            InitializeComponent();
            string handoffRoot = Path.Combine(ApplicationData.Current.LocalFolder.Path, "ExplorerHandoff");
            _explorerHandoffService = new ExplorerHandoffService(handoffRoot);
            UnhandledException += App_UnhandledException;
        }

        private static void EnsurePerMonitorDpiAwareness()
        {
            try { _ = SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2); }
            catch { }
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            AppActivationArguments? activation = GetCurrentActivationArguments();
            if (!EnsurePrimaryInstance(activation)) return;

            bool explorerInspectionActivation = HandleExplorerInspectionActivation(activation, allowProcessArguments: true);
            Stopwatch startupTimer = Stopwatch.StartNew();
            bool launchedByWindowsStartup = activation?.Kind == ExtendedActivationKind.StartupTask;
            _ = _diagnosticLog.InformationAsync("ApplicationLaunch",
                launchedByWindowsStartup ? "Sentinel AI Windows startup launch started." : "Sentinel AI interactive launch started.");

            try
            {
                WindowsStartupRegistrationService.StartupRegistrationResult startup = _startupRegistrationService.EnsureRegisteredAndVerify();
                _ = startup.Registered
                    ? _diagnosticLog.InformationAsync("WindowsStartup", startup.Summary)
                    : _diagnosticLog.WarningAsync("WindowsStartup", startup.Summary);

                MainWindow mainWindow = new();
                mainWindow.EnsureMonitoringSchedulerRunning();
                mainWindow.Activated += (_, _) => mainWindow.CheckForMandatoryStoreUpdate();
                _window = mainWindow;
                _window.AppWindow.Closing += MainAppWindow_Closing;
                _systemTrayService = new SystemTrayService(ShowMainWindow, ShowOptionsWindow, ExitApplication);

                if (explorerInspectionActivation)
                {
                    // Explorer commands are intentionally dialog-only. Keep the dashboard hidden;
                    // MainWindow owns the command handlers but presents them through a dedicated
                    // compact dialog host instead of activating the full Sentinel dashboard.
                    _pendingInteractiveActivation = false;
                    _window.AppWindow.Hide();
                }
                else if (launchedByWindowsStartup && !_pendingInteractiveActivation)
                {
                    mainWindow.StartBackgroundMonitoring();
                    _window.AppWindow.Hide();
                    _ = _diagnosticLog.InformationAsync("WindowsStartup", "Sentinel AI started with Windows and is monitoring from the system tray.");
                }
                else
                {
                    _pendingInteractiveActivation = false;
                    PromptForExplorerRestartAfterInstall();
                    _window.Activate();
                }

                DeliverPendingExplorerInspection(mainWindow);

                startupTimer.Stop();
                _ = _diagnosticLog.InformationAsync("StartupPerformance",
                    explorerInspectionActivation
                        ? $"Explorer dialog activation completed in {startupTimer.ElapsedMilliseconds} ms without opening the dashboard."
                        : launchedByWindowsStartup
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
                // The asynchronous diagnostic write may not complete before a fatal startup
                // exception terminates the process. Persist a bounded synchronous breadcrumb
                // first so a launch failure from an installed MSIX always leaves evidence.
                _diagnosticLog.WriteCrashBreadcrumb("ApplicationLaunchFailure", ex);
                _ = _diagnosticLog.ErrorAsync("ApplicationLaunchFailure",
                    $"Sentinel AI could not complete startup after {startupTimer.ElapsedMilliseconds} ms.", ex);
                throw;
            }
        }

        private bool EnsurePrimaryInstance(AppActivationArguments? activation)
        {
            try
            {
                AppInstance current = AppInstance.GetCurrent();
                AppInstance primary = AppInstance.FindOrRegisterForKey(MainInstanceKey);
                if (!primary.IsCurrent)
                {
                    AppActivationArguments? redirect = activation ?? GetCurrentActivationArguments();
                    if (redirect is not null)
                        primary.RedirectActivationToAsync(redirect).AsTask().GetAwaiter().GetResult();
                    _ = _diagnosticLog.InformationAsync("SingleInstance", "A duplicate Sentinel AI launch was redirected to the existing instance.");
                    Exit();
                    return false;
                }

                _primaryInstance = primary;
                _primaryInstance.Activated -= PrimaryInstance_Activated;
                _primaryInstance.Activated += PrimaryInstance_Activated;
                return true;
            }
            catch (Exception ex)
            {
                _ = _diagnosticLog.WarningAsync("SingleInstance", "Single-instance coordination failed; Sentinel will continue in this process. " + ex.Message);
                return true;
            }
        }

        private static AppActivationArguments? GetCurrentActivationArguments()
        {
            try { return AppInstance.GetCurrent().GetActivatedEventArgs(); }
            catch { return null; }
        }

        private bool HandleExplorerInspectionActivation(AppActivationArguments? activation, bool allowProcessArguments)
        {
            string? handoffToken = ExplorerCommandLineParser.TryGetHandoffToken(activation, allowProcessArguments);
            if (!string.IsNullOrWhiteSpace(handoffToken))
            {
                _ = _diagnosticLog.InformationAsync("ExplorerCommand", "Explorer handoff activation received.");
                if (_explorerHandoffService.TryConsume(handoffToken, out ExplorerInspectionRequest? request) && request is not null)
                {
                    _pendingExplorerInspection = request;
                    return true;
                }

                _ = _diagnosticLog.WarningAsync("ExplorerCommand", "Explorer handoff token was invalid, stale, already consumed, or unavailable.");
            }

            if (allowProcessArguments && ExplorerCommandLineParser.ContainsLegacyDirectPathArguments())
            {
                _ = _diagnosticLog.WarningAsync("ExplorerCommand", "Legacy raw-path Explorer command arguments were rejected. Sentinel requires an authenticated handoff token.");
                return true;
            }

            return false;
        }

        private void PrimaryInstance_Activated(object? sender, AppActivationArguments e)
        {
            bool explorerInspectionActivation = HandleExplorerInspectionActivation(e, allowProcessArguments: false);
            if (explorerInspectionActivation)
            {
                if (_window is MainWindow mainWindow)
                {
                    _window.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_pendingExplorerInspection is not null)
                        {
                            _window.AppWindow.Hide();
                            DeliverPendingExplorerInspection(mainWindow);
                        }
                    });
                }
                return;
            }

            _pendingInteractiveActivation = true;
            if (_window is null)
            {
                _window = new MainWindow();
                _window.AppWindow.Closing += MainAppWindow_Closing;
            }

            _window.DispatcherQueue.TryEnqueue(ShowMainWindow);
        }

        private void DeliverPendingExplorerInspection(MainWindow mainWindow)
        {
            ExplorerInspectionRequest? request = _pendingExplorerInspection;
            if (request is null) return;
            _pendingExplorerInspection = null;
            mainWindow.DispatcherQueue.TryEnqueue(() => mainWindow.HandleExplorerInspectionRequest(request));
        }

        private void PromptForExplorerRestartAfterInstall()
        {
            try
            {
                ExplorerRestartPromptState promptState = ExplorerRestartPromptState.Load();
                if (promptState.ShouldPrompt())
                {
                    _ = _diagnosticLog.InformationAsync("ExplorerIntegration", "Explorer integration restart prompt is pending after package installation.");
                    if (_window is MainWindow mainWindow)
                        mainWindow.ShowExplorerRestartPrompt(promptState);
                }
            }
            catch (Exception ex)
            {
                _ = _diagnosticLog.WarningAsync("ExplorerIntegration", "Explorer restart prompt check failed. " + ex.Message);
            }
        }

        private void MainAppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isExplicitExit) return;
            args.Cancel = true;
            sender.Hide();
            _ = _diagnosticLog.InformationAsync("ApplicationLifecycle", "Main window closed to the system tray; Sentinel continues monitoring.");
        }

        private void ShowMainWindow()
        {
            if (_window is null)
            {
                _window = new MainWindow();
                _window.AppWindow.Closing += MainAppWindow_Closing;
            }

            _pendingInteractiveActivation = false;
            _window.Activate();
            _ = _diagnosticLog.InformationAsync("ApplicationLifecycle", "Main window activated from the system tray or secondary launch.");
        }

        private void ShowOptionsWindow()
        {
            if (_optionsWindow is null)
            {
                _optionsWindow = new OptionsWindow();
                _optionsWindow.Closed += (_, _) => _optionsWindow = null;
            }

            _optionsWindow.Activate();
        }

        private void ExitApplication()
        {
            _isExplicitExit = true;
            _systemTrayService?.Dispose();
            _systemTrayService = null;
            _optionsWindow?.Close();
            _optionsWindow = null;
            _window?.Close();
            Exit();
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            _diagnosticLog.WriteCrashBreadcrumb("UnhandledException", e.Exception);
            _ = _diagnosticLog.ErrorAsync("UnhandledException", "An unhandled UI exception reached the application boundary.", e.Exception);
        }
    }
}
