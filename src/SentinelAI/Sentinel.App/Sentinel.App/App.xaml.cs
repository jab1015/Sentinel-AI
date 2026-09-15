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
                _ = _diagnosticLog.ErrorAsync("SingleInstanceFailure",
                    "Sentinel AI could not establish its single-instance activation boundary.", ex);
                throw;
            }
        }

        private void PrimaryInstance_Activated(object? sender, AppActivationArguments args)
        {
            if (args.Kind == ExtendedActivationKind.StartupTask)
            {
                _ = _diagnosticLog.InformationAsync("SingleInstance", "A duplicate Windows startup activation was ignored because Sentinel AI is already running.");
                return;
            }

            bool explorerInspectionActivation = HandleExplorerInspectionActivation(args, allowProcessArguments: false);
            Window? window = _window;
            if (window is null)
            {
                _pendingInteractiveActivation = !explorerInspectionActivation;
                return;
            }

            if (explorerInspectionActivation && window is MainWindow mainWindow)
            {
                // Do not surface the dashboard merely because Explorer invoked a command.
                // If the user already has the dashboard open, leave its state alone; otherwise
                // the command is presented only through the dedicated compact dialog host.
                DeliverPendingExplorerInspection(mainWindow);
                _ = _diagnosticLog.InformationAsync("SingleInstance", "The existing Sentinel AI instance handled an Explorer command without opening the dashboard.");
                return;
            }

            ShowMainWindow();
            _ = _diagnosticLog.InformationAsync("SingleInstance", "The existing Sentinel AI window handled a redirected activation.");
        }

        private bool HandleExplorerInspectionActivation(AppActivationArguments? activation, bool allowProcessArguments)
        {
            Guid handoffId = Guid.Empty;
            bool hasRequest = false;

            if (activation?.Kind == ExtendedActivationKind.Launch && activation.Data is ILaunchActivatedEventArgs launchArgs)
                hasRequest = ExplorerHandoffService.TryParseHandoffId(launchArgs.Arguments, out handoffId);

            if (!hasRequest && allowProcessArguments)
            {
                string[] processArguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
                hasRequest = ExplorerHandoffService.TryParseHandoffId(processArguments, out handoffId);
            }

            if (!hasRequest) return false;

            if (_explorerHandoffService.TryConsume(handoffId, out ExplorerInspectionRequest? request, out string reason) && request is not null)
            {
                _pendingExplorerInspection = request;
                _ = _diagnosticLog.InformationAsync("ExplorerInspectionActivation",
                    $"Sentinel accepted a one-time File Explorer inspection handoff containing {request.Paths.Count} revalidated filesystem item(s). No file was changed by activation.");
            }
            else
            {
                _pendingExplorerInspection = null;
                _ = _diagnosticLog.WarningAsync("ExplorerInspectionActivation",
                    $"Sentinel rejected a File Explorer inspection handoff. {reason}");
            }

            return true;
        }

        private void DeliverPendingExplorerInspection(MainWindow mainWindow)
        {
            ExplorerInspectionRequest? request = _pendingExplorerInspection;
            if (request is null) return;
            _pendingExplorerInspection = null;
            mainWindow.HandleExplorerInspectionRequest(request);
        }

        private static AppActivationArguments? GetCurrentActivationArguments()
        {
            try { return AppInstance.GetCurrent().GetActivatedEventArgs(); }
            catch { return null; }
        }

        private void MainAppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isExplicitExit) return;
            args.Cancel = true;
            sender.Hide();
            _ = _diagnosticLog.InformationAsync("SystemTray", "Main window hidden. Sentinel AI continues monitoring in the system tray.");
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
                if (_primaryInstance is not null) _primaryInstance.Activated -= PrimaryInstance_Activated;
                _systemTrayService?.Dispose();
                _systemTrayService = null;
                Exit();
                return;
            }

            window.DispatcherQueue.TryEnqueue(() =>
            {
                _isExplicitExit = true;
                if (_primaryInstance is not null) _primaryInstance.Activated -= PrimaryInstance_Activated;
                _optionsWindow?.Close();
                _optionsWindow = null;
                _systemTrayService?.Dispose();
                _systemTrayService = null;
                window.Close();
                Exit();
            });
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            _diagnosticLog.WriteCrashBreadcrumb("UnhandledException", e.Exception);
            _ = _diagnosticLog.ErrorAsync(
                "UnhandledException",
                "An unhandled application exception reached the WinUI application boundary.",
                e.Exception);
        }
    }
}
