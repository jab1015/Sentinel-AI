using System;
using Windows.ApplicationModel;

namespace Sentinel.App.Services;

/// <summary>
/// Central boundary for Microsoft Store-only runtime APIs.
/// A locally generated/sideloaded MSIX normally has a Developer signature, not a
/// Microsoft Store retail signature. StoreContext APIs are therefore never touched
/// from those packages; free/local Sentinel functionality remains available instead.
/// </summary>
internal static class MicrosoftStoreRuntimePolicy
{
    public static bool IsStoreSignedPackage(out string diagnostic)
    {
        try
        {
            Package package = Package.Current;
            if (string.IsNullOrWhiteSpace(package.Id.FamilyName))
            {
                diagnostic = "The current process does not have a usable package identity.";
                return false;
            }

            if (package.SignatureKind != PackageSignatureKind.Store)
            {
                diagnostic = $"Package signature kind is {package.SignatureKind}; Microsoft Store runtime APIs are disabled for this sideloaded/test package.";
                return false;
            }

            diagnostic = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"Microsoft Store package identity could not be verified ({ex.GetType().Name}).";
            return false;
        }
    }
}
