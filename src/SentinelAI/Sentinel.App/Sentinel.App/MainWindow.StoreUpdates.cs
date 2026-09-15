using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sentinel.App.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App;

public sealed partial class MainWindow
{
    private readonly SemaphoreSlim _storeUpdateCheckGate = new(1, 1);
    private readonly DiagnosticLogService _storeUpdateDiagnosticLog = new();
    private StoreUpdateService? _storeUpdateService;
    private StoreMandatoryUpdateState? _lastStoreUpdateState;
    private DateTimeOffset _lastStoreUpdateCheckUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Checks Partner Center mandatory-update state before allowing normal interactive use.
    /// Background monitoring is deliberately not stopped if the user postpones or Store is
    /// temporarily unavailable; reopening the dashboard re-enforces any known mandatory update.
    /// </summary>
    internal void CheckForMandatoryStoreUpdate()
    {
        DispatcherQueue.TryEnqueue(() => _ = CheckForMandatoryStoreUpdateAsync());
    }

    private async Task CheckForMandatoryStoreUpdateAsync()
    {
        if (!await _storeUpdateCheckGate.WaitAsync(0).ConfigureAwait(true))
            return;

        try
        {
            FrameworkElement? root = Content as FrameworkElement;
            if (root?.XamlRoot is null)
                return;

            _storeUpdateService ??= new StoreUpdateService(WinRT.Interop.WindowNative.GetWindowHandle(this));

            StoreMandatoryUpdateState state;
            bool refresh = _lastStoreUpdateState is null ||
                           (!_lastStoreUpdateState.IsMandatory &&
                            DateTimeOffset.UtcNow - _lastStoreUpdateCheckUtc >= TimeSpan.FromMinutes(30));
            if (refresh)
            {
                state = await _storeUpdateService.CheckForMandatoryUpdateAsync().ConfigureAwait(true);
                _lastStoreUpdateState = state;
                _lastStoreUpdateCheckUtc = DateTimeOffset.UtcNow;
            }
            else
            {
                state = _lastStoreUpdateState!;
            }

            if (!state.StoreCheckSucceeded)
            {
                await _storeUpdateDiagnosticLog.WarningAsync("StoreUpdateCheck", state.Message + " " + state.Diagnostic);
                return;
            }

            if (!state.IsMandatory)
                return;

            await _storeUpdateDiagnosticLog.WarningAsync("MandatoryStoreUpdate",
                "Microsoft Store reports a mandatory Sentinel AI update. Interactive use is gated until the update is installed; background protection remains running if the user postpones.");

            ContentDialog requiredDialog = new()
            {
                Title = "Sentinel AI update required",
                Content = "Microsoft Store has a required Sentinel AI update. Install it now to continue using the Sentinel dashboard. If you choose Not now, Sentinel will keep its current background monitoring running, but the dashboard will remain unavailable until you update.",
                PrimaryButtonText = "Update now",
                CloseButtonText = "Not now",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root.XamlRoot
            };

            ContentDialogResult choice = await requiredDialog.ShowAsync();
            if (choice != ContentDialogResult.Primary)
            {
                AppWindow.Hide();
                return;
            }

            StoreMandatoryUpdateInstallResult install =
                await _storeUpdateService.InstallMandatoryUpdateAsync(state).ConfigureAwait(true);
            if (install.Succeeded)
            {
                _lastStoreUpdateState = null;
                await _storeUpdateDiagnosticLog.InformationAsync("MandatoryStoreUpdate",
                    "Microsoft Store completed the required Sentinel AI update request.");
                return;
            }

            await _storeUpdateDiagnosticLog.WarningAsync("MandatoryStoreUpdateFailure", install.Message);
            ContentDialog failedDialog = new()
            {
                Title = "Required update not installed",
                Content = install.Message + " Sentinel will keep background monitoring running. Open Sentinel AI again when Microsoft Store is available to retry the required update.",
                PrimaryButtonText = "Retry",
                CloseButtonText = "Keep monitoring",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root.XamlRoot
            };

            if (await failedDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _lastStoreUpdateState = state;
                DispatcherQueue.TryEnqueue(() => _ = CheckForMandatoryStoreUpdateAsync());
            }
            else
            {
                AppWindow.Hide();
            }
        }
        catch (Exception ex)
        {
            // Store/update UI failure must not crash or turn off the security monitor.
            await _storeUpdateDiagnosticLog.ErrorAsync("MandatoryStoreUpdateBoundary",
                "Sentinel could not complete the Microsoft Store mandatory-update check. Background monitoring remains active.", ex);
        }
        finally
        {
            _storeUpdateCheckGate.Release();
        }
    }
}
