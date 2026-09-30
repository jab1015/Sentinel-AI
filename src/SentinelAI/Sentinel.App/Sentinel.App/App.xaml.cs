using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Sentinel.App.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace Sentinel.App
{
    public partial class App : Application
    {
        private const string ProductionMainInstanceKey = "SentinelAI.Main";
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
            BootstrapLaunchLog.Write("App.ctor.enter");
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            EnsurePerMonitorDpiAwareness();
            BootstrapLaunchLog.Write("App.InitializeComponent.start");
            InitializeComponent();
            BootstrapLaunchLog.Write("App.InitializeComponent.complete");

            string handoffRoot = ResolveExplorerHandoffRoot();
            BootstrapLaunchLog.Write($"App.ExplorerHandoff.root={handoffRoot}");
            _explorerHandoffService = new ExplorerHandoffService(handoffRoot);
            UnhandledException += App_UnhandledException;
            BootstrapLaunchLog.Write("App.ctor.complete");
        }

        private static void EnsurePerMonitorDpiAwareness()
        {
            try { _ = SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2); }
            catch { }
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            BootstrapLaunchLog.Write("App.OnLaunched.enter");
            Stopwatch startupTimer = Stopwatch.StartNew();
            AppActivationArguments? activation = GetCurrentActivationArguments();
            bool launchedByWindowsStartup = activation?.Kind == ExtendedActivationKind.StartupTask;

            _ = _diagnosticLog.InformationAsync("ApplicationLaunch",
                launchedByWindowsStartup ? "Sentinel AI Windows startup launch started." : "Sentinel AI interactive launch started.");

            try
            {
                BootstrapLaunchLog.Write("App.SingleInstance.start");
                if (!EnsurePrimaryInstance(activation))
                {
                    BootstrapLaunchLog.Write("App.SingleInstance.redirected-exit");
                    return;
                }
                BootstrapLaunchLog.Write("App.SingleInstance.complete");

                bool explorerInspectionActivation = false;
                try
                {
                    explorerInspectionActivation = HandleExplorerInspectionActivation(activation, allowProcessArguments: true);
                }
                catch (Exception ex)
                {
                    _ = _diagnosticLog.WarningAsync("ExplorerInspectionActivation",
                        $"Explorer activation parsing failed ({ex.GetType().Name}). Sentinel will continue normal startup.");
                }

                BootstrapLaunchLog.Write("App.MainWindow.construct.start");
                MainWindow mainWindow = new();
                BootstrapLaunchLog.Write("App.MainWindow.construct.complete");
                _window = mainWindow;
                _window.AppWindow.Closing += MainAppWindow_Closing;
                BootstrapLaunchLog.Write("App.MainWindow.assigned");

                // Make an interactive launch visible before initializing optional services.
                // A tray-icon, Store, startup-registration, or monitoring setup failure must
                // never make an otherwise healthy installed app appear to do nothing.
                if (explorerInspectionActivation)
                {
                    _pendingInteractiveActivation = false;
                    _window.AppWindow.Hide();
                }
                else if (launchedByWindowsStartup && !_pendingInteractiveActivation)
                {
                    _window.AppWindow.Hide();
                }
                else
                {
                    _pendingInteractiveActivation = false;
                    BootstrapLaunchLog.Write("App.MainWindow.show.start");
                    _window.AppWindow.Show();
                    _window.Activate();
                    BootstrapLaunchLog.Write("App.MainWindow.show.complete");
                }

                _ = InitializeWindowsStartupRegistrationAfterWindowAsync();

                try
                {
                    mainWindow.EnsureMonitoringSchedulerRunning();
                    if (launchedByWindowsStartup && !explorerInspectionActivation)
                        mainWindow.StartBackgroundMonitoring();
                }
                catch (Exception ex)
                {
                    _ = _diagnosticLog.ErrorAsync("MonitoringStartupFailure",
                        "Sentinel opened, but background monitoring initialization failed. The dashboard remains available.", ex);
                }

                try
                {
                    mainWindow.Activated += (_, _) => mainWindow.CheckForMandatoryStoreUpdate();
                }
                catch (Exception ex)
                {
                    _ = _diagnosticLog.WarningAsync("StoreUpdateHookFailure",
                        $"The Store update hook could not be initialized ({ex.GetType().Name}). Sentinel will continue running.");
                }

                TryInitializeSystemTray();

#if !SENTINEL_LOCAL_DEV
                if (!explorerInspectionActivation && !launchedByWindowsStartup)
                {
                    try
                    {
                        PromptForExplorerRestartAfterInstall();
                    }
                    catch (Exception ex)
                    {
                        _ = _diagnosticLog.WarningAsync("ExplorerRestartPromptFailure",
                            $"The Explorer restart prompt could not be shown ({ex.GetType().Name}). Sentinel will continue running.");
                    }
                }
#else
                // The LocalDev VM installer owns the reboot prompt. Avoid a second
                // unowned native dialog during first launch, which can sit behind the
                // installer and make the activated app appear hung.
                _ = _diagnosticLog.InformationAsync(
                    "ExplorerRestartPrompt",
                    "LocalDev VM build: reboot prompting is handled by the VM installer after launch verification.");
#endif

                try
                {
                    DeliverPendingExplorerInspection(mainWindow);
                }
                catch (Exception ex)
                {
                    _ = _diagnosticLog.ErrorAsync("ExplorerInspectionDeliveryFailure",
                        "Sentinel opened, but the pending Explorer inspection request could not be delivered.", ex);
                }

                if (launchedByWindowsStartup && !explorerInspectionActivation)
                {
                    _ = _diagnosticLog.InformationAsync("WindowsStartup",
                        "Sentinel AI started with Windows and is monitoring in the background.");
                }

                startupTimer.Stop();
                BootstrapLaunchLog.Write($"App.OnLaunched.complete elapsedMs={startupTimer.ElapsedMilliseconds}");
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
                BootstrapLaunchLog.Write($"App.OnLaunched.failure elapsedMs={startupTimer.ElapsedMilliseconds}", ex);
                _systemTrayService?.Dispose();
                _systemTrayService = null;
                _diagnosticLog.WriteCrashBreadcrumb("ApplicationLaunchFailure", ex);
                _ = _diagnosticLog.ErrorAsync("ApplicationLaunchFailure",
                    $"Sentinel AI could not complete startup after {startupTimer.ElapsedMilliseconds} ms.", ex);

                // If the main window already exists, keep it alive instead of turning an
                // optional initialization failure into a silent process exit.
                if (_window is not null)
                {
                    try
                    {
                        _window.AppWindow.Show();
                        _window.Activate();
                        return;
                    }
                    catch
                    {
                    }
                }

                throw;
            }
        }

        private async Task InitializeWindowsStartupRegistrationAfterWindowAsync()
        {
            try
            {
                // Never block first paint on StartupTask/WinRT work.
                await Task.Yield();
                BootstrapLaunchLog.Write("App.WindowsStartupRegistration.start");
                WindowsStartupRegistrationService.StartupRegistrationResult startup =
                    await _startupRegistrationService.EnsureRegisteredAndVerifyAsync();
                BootstrapLaunchLog.Write("App.WindowsStartupRegistration.complete");
                _ = startup.Registered
                    ? _diagnosticLog.InformationAsync("WindowsStartup", startup.Summary)
                    : _diagnosticLog.WarningAsync("WindowsStartup", startup.Summary);
            }
            catch (Exception ex)
            {
                BootstrapLaunchLog.Write("App.WindowsStartupRegistration.failure", ex);
                _ = _diagnosticLog.WarningAsync("WindowsStartup",
                    $"Startup registration could not be initialized ({ex.GetType().Name}). Sentinel will continue running.");
            }
        }

        private void TryInitializeWindowsStartupRegistration()
        {
            try
            {
                WindowsStartupRegistrationService.StartupRegistrationResult startup = _startupRegistrationService.EnsureRegisteredAndVerify();
                _ = startup.Registered
                    ? _diagnosticLog.InformationAsync("WindowsStartup", startup.Summary)
                    : _diagnosticLog.WarningAsync("WindowsStartup", startup.Summary);
            }
            catch (Exception ex)
            {
                _ = _diagnosticLog.WarningAsync("WindowsStartup",
                    $"Startup registration could not be initialized ({ex.GetType().Name}). Sentinel will continue running.");
            }
        }

        private void TryInitializeSystemTray()
        {
            try
            {
                _systemTrayService = new SystemTrayService(ShowMainWindow, ShowOptionsWindow, ExitApplication);
            }
            catch (Exception ex)
            {
                _systemTrayService?.Dispose();
                _systemTrayService = null;
                _ = _diagnosticLog.ErrorAsync("SystemTrayInitializationFailure",
                    "The system-tray icon could not be initialized. Sentinel will remain usable from its main window.", ex);
            }
        }

        private bool EnsurePrimaryInstance(AppActivationArguments? activation)
        {
            try
            {
                string instanceKey = GetMainInstanceKey();
                BootstrapLaunchLog.Write($"App.SingleInstance.key={instanceKey}");
                AppInstance current = AppInstance.GetCurrent();
                AppInstance primary = AppInstance.FindOrRegisterForKey(instanceKey);
                if (!primary.IsCurrent)
                {
                    AppActivationArguments? redirect = activation ?? GetCurrentActivationArguments();
                    if (redirect is not null)
                    {
#if SENTINEL_LOCAL_DEV
                        Task redirectTask = primary.RedirectActivationToAsync(redirect).AsTask();
                        if (!redirectTask.Wait(TimeSpan.FromSeconds(3)))
                        {
                            BootstrapLaunchLog.Write("App.SingleInstance.redirect-timeout; continuing LocalDev recovery instance");
                            _ = _diagnosticLog.WarningAsync(
                                "SingleInstance",
                                "The existing LocalDev Sentinel instance did not accept activation within 3 seconds. A recovery window will open instead of silently exiting.");
                            _primaryInstance = null;
                            return true;
                        }
                        redirectTask.GetAwaiter().GetResult();
#else
                        primary.RedirectActivationToAsync(redirect).AsTask().GetAwaiter().GetResult();
#endif
                    }
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
                _ = _diagnosticLog.WarningAsync("SingleInstanceFailure",
                    $"Sentinel AI could not establish its single-instance activation boundary ({ex.GetType().Name}). Startup will continue without redirection.");
                _primaryInstance = null;
                return true;
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

        private static string GetMainInstanceKey()
        {
#if SENTINEL_LOCAL_DEV
            return HasPackageIdentity()
                ? "SentinelAI.Main.LocalDev.Packaged"
                : "SentinelAI.Main.LocalDev.Unpackaged";
#else
            return ProductionMainInstanceKey;
#endif
        }

        private static bool HasPackageIdentity()
        {
            try
            {
                return !string.IsNullOrWhiteSpace(Package.Current.Id.FamilyName);
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveExplorerHandoffRoot()
        {
            try
            {
                string packageLocal = ApplicationData.Current.LocalFolder.Path;
                if (!string.IsNullOrWhiteSpace(packageLocal))
                    return Path.Combine(packageLocal, "ExplorerHandoff");
            }
            catch (Exception ex)
            {
                BootstrapLaunchLog.Write("App.ApplicationData.LocalFolder.unavailable; using LocalAppData fallback", ex);
            }

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Modern Methods", "Sentinel AI", "ExplorerHandoff");
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

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            _diagnosticLog.WriteCrashBreadcrumb("AppDomainUnhandledException", e.ExceptionObject as Exception);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            _diagnosticLog.WriteCrashBreadcrumb("UnobservedTaskException", e.Exception);
        }
    }
}
