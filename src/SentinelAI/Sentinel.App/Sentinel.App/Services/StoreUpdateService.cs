using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace Sentinel.App.Services;

/// <summary>
/// Microsoft Store application-update boundary. Production Store builds can discover
/// Partner Center mandatory updates and request their installation. Development and
/// sideloaded test packages never initialize StoreContext, so a local MSIX can always
/// launch and exercise Sentinel's free/local functionality without a retail Store license.
/// </summary>
public sealed class StoreUpdateService
{
    private readonly object _storeContextGate = new();
    private readonly nint _ownerWindowHandle;
    private StoreContext? _storeContext;
    private bool _windowInitialized;

    public StoreUpdateService(nint ownerWindowHandle)
    {
        _ownerWindowHandle = ownerWindowHandle;
    }

    public async Task<StoreMandatoryUpdateState> CheckForMandatoryUpdateAsync()
    {
#if DEBUG || SENTINEL_LOCAL_DEV
        return StoreMandatoryUpdateState.NotApplicable("Store mandatory-update enforcement is disabled in development builds.");
#else
        if (!MicrosoftStoreRuntimePolicy.IsStoreSignedPackage(out string policyDiagnostic))
        {
            return StoreMandatoryUpdateState.NotApplicable(
                "Store mandatory-update enforcement is disabled for this sideloaded/test package.",
                policyDiagnostic);
        }

        if (!TryGetStoreContext(out StoreContext? storeContext, out string contextDiagnostic) || storeContext is null)
        {
            return StoreMandatoryUpdateState.Unavailable(
                "Sentinel could not initialize Microsoft Store update services right now.",
                contextDiagnostic);
        }

        try
        {
            IReadOnlyList<StorePackageUpdate> updates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
            StorePackageUpdate[] mandatory = updates.Where(update => update.Mandatory).ToArray();
            return mandatory.Length == 0
                ? StoreMandatoryUpdateState.Current()
                : StoreMandatoryUpdateState.Required(mandatory);
        }
        catch (Exception ex)
        {
            // A transient Store/network failure must never masquerade as a mandatory update.
            // Sentinel keeps protecting the PC and retries on the next interactive activation.
            return StoreMandatoryUpdateState.Unavailable(
                "Sentinel could not check Microsoft Store for required application updates right now.",
                ex.Message);
        }
#endif
    }

    public async Task<StoreMandatoryUpdateInstallResult> InstallMandatoryUpdateAsync(
        StoreMandatoryUpdateState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.IsMandatory || state.Updates.Count == 0)
            return new(false, false, "No mandatory Sentinel AI update is pending.");
        if (!MicrosoftStoreRuntimePolicy.IsStoreSignedPackage(out _))
            return new(false, true, "Required Store updates can be installed only by the Microsoft Store-signed Sentinel package.");
        if (!TryGetStoreContext(out StoreContext? storeContext, out string contextDiagnostic) || storeContext is null)
            return new(false, true, "Microsoft Store update services are unavailable. " + contextDiagnostic);

        try
        {
            StorePackageUpdateResult result =
                await storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(state.Updates);

            if (result.OverallState == StorePackageUpdateState.Completed)
                return new(true, false, "The required Sentinel AI update was installed.");

            bool mandatoryFailure = state.Updates.Any(update =>
                result.StorePackageUpdateStatuses.Any(status =>
                    status.PackageUpdateState != StorePackageUpdateState.Completed &&
                    string.Equals(status.PackageFamilyName, update.Package.Id.FamilyName, StringComparison.OrdinalIgnoreCase)));

            return new(false, mandatoryFailure,
                mandatoryFailure
                    ? "The required Sentinel AI update was not installed."
                    : "Microsoft Store did not complete the Sentinel AI update.");
        }
        catch (Exception ex)
        {
            return new(false, true,
                "Microsoft Store could not install the required Sentinel AI update. " + ex.Message);
        }
    }

    private bool TryGetStoreContext(out StoreContext? storeContext, out string diagnostic)
    {
        lock (_storeContextGate)
        {
            try
            {
                _storeContext ??= StoreContext.GetDefault();
                if (_storeContext is null)
                {
                    storeContext = null;
                    diagnostic = "Microsoft Store returned no StoreContext.";
                    return false;
                }

                if (!_windowInitialized && _ownerWindowHandle != 0)
                {
                    WinRT.Interop.InitializeWithWindow.Initialize(_storeContext, _ownerWindowHandle);
                    _windowInitialized = true;
                }

                storeContext = _storeContext;
                diagnostic = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                storeContext = null;
                diagnostic = $"Microsoft Store context initialization failed ({ex.GetType().Name}): {ex.Message}";
                return false;
            }
        }
    }
}

public sealed record StoreMandatoryUpdateState(
    bool IsMandatory,
    bool StoreCheckSucceeded,
    IReadOnlyList<StorePackageUpdate> Updates,
    string Message,
    string Diagnostic = "")
{
    public static StoreMandatoryUpdateState Current() =>
        new(false, true, Array.Empty<StorePackageUpdate>(), "Sentinel AI is current.");

    public static StoreMandatoryUpdateState Required(IReadOnlyList<StorePackageUpdate> updates) =>
        new(true, true, updates, "A required Sentinel AI update is available from Microsoft Store.");

    public static StoreMandatoryUpdateState NotApplicable(string message, string diagnostic = "") =>
        new(false, true, Array.Empty<StorePackageUpdate>(), message, diagnostic);

    public static StoreMandatoryUpdateState Unavailable(string message, string diagnostic) =>
        new(false, false, Array.Empty<StorePackageUpdate>(), message, diagnostic);
}

public sealed record StoreMandatoryUpdateInstallResult(
    bool Succeeded,
    bool MandatoryUpdateStillRequired,
    string Message);
