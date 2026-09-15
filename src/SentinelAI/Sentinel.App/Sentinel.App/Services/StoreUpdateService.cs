using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace Sentinel.App.Services;

/// <summary>
/// Microsoft Store application-update boundary. Production Store builds can discover
/// Partner Center mandatory updates and request their installation. Development builds
/// never contact Store update enforcement so local testing cannot accidentally update
/// itself into a production package.
/// </summary>
public sealed class StoreUpdateService
{
    private readonly StoreContext _storeContext;

    public StoreUpdateService(nint ownerWindowHandle)
    {
        _storeContext = StoreContext.GetDefault();
        if (ownerWindowHandle != 0)
        {
            try { WinRT.Interop.InitializeWithWindow.Initialize(_storeContext, ownerWindowHandle); }
            catch { /* Discovery can still report a useful Store-unavailable result below. */ }
        }
    }

    public async Task<StoreMandatoryUpdateState> CheckForMandatoryUpdateAsync()
    {
#if DEBUG || SENTINEL_LOCAL_DEV
        return StoreMandatoryUpdateState.NotApplicable("Store mandatory-update enforcement is disabled in development builds.");
#else
        if (!HasPackageIdentity())
            return StoreMandatoryUpdateState.NotApplicable("Store update enforcement requires the installed Microsoft Store package.");

        try
        {
            IReadOnlyList<StorePackageUpdate> updates = await _storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
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

        try
        {
            StorePackageUpdateResult result =
                await _storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(state.Updates);

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

    private static bool HasPackageIdentity()
    {
        try { return !string.IsNullOrWhiteSpace(Package.Current.Id.FamilyName); }
        catch (InvalidOperationException) { return false; }
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

    public static StoreMandatoryUpdateState NotApplicable(string message) =>
        new(false, true, Array.Empty<StorePackageUpdate>(), message);

    public static StoreMandatoryUpdateState Unavailable(string message, string diagnostic) =>
        new(false, false, Array.Empty<StorePackageUpdate>(), message, diagnostic);
}

public sealed record StoreMandatoryUpdateInstallResult(
    bool Succeeded,
    bool MandatoryUpdateStillRequired,
    string Message);
