using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sentinel.App.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace Sentinel.App;

public sealed partial class MainWindow
{
    private async Task ShowExplorerPremiumPrivacyRequestAsync(
        ExplorerRequestedAction action,
        string path,
        FrameworkElement rootElement)
    {
        switch (action)
        {
            case ExplorerRequestedAction.EncryptFile:
                await EncryptExplorerFileAsync(path, rootElement).ConfigureAwait(true);
                return;
            case ExplorerRequestedAction.DecryptFile:
                await DecryptExplorerFileAsync(path, rootElement).ConfigureAwait(true);
                return;
            case ExplorerRequestedAction.AddToVault:
                await AddExplorerFileToVaultAsync(path, rootElement).ConfigureAwait(true);
                return;
            case ExplorerRequestedAction.SecureDelete:
                await SecureDeleteExplorerFileAsync(path, rootElement).ConfigureAwait(true);
                return;
            default:
                throw new InvalidOperationException("Unsupported Explorer Premium Privacy action.");
        }
    }

    internal async Task DecryptExplorerFileAsync(string path, FrameworkElement rootElement)
    {
        const string suffix = ".sentinel.senc";
        if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            await ShowPrivacyMessageAsync(rootElement, "Not a Sentinel encrypted file",
                "Decrypt is available for files ending in .sentinel.senc. No file was changed.");
            return;
        }

        string output = path[..^suffix.Length];
        if (File.Exists(output) || Directory.Exists(output))
        {
            await ShowPrivacyMessageAsync(rootElement, "Decryption output already exists",
                $"Sentinel will not overwrite an existing plaintext file. Move or rename this file first:\n\n{output}\n\nThen try Decrypt again.");
            return;
        }

        ContentDialog confirmation = new()
        {
            Title = "Decrypt Sentinel File",
            Content = $"Encrypted container:\n{path}\n\nRestored plaintext output:\n{output}\n\nSentinel will authenticate the encrypted container before accepting the restored file. The encrypted container will remain unchanged. Decryption of your existing Sentinel data does not require a current subscription.",
            PrimaryButtonText = "Decrypt",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = rootElement.XamlRoot
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        FileEncryptionService encryption = new();
        FileDecryptionResult result = await encryption.DecryptAsync(
            path,
            output,
            new IFileKeyProtector[] { new WindowsCurrentUserFileKeyProtector() }).ConfigureAwait(true);

        await ShowPrivacyMessageAsync(
            rootElement,
            result.Succeeded ? "Decrypted and verified" : "Decryption did not complete",
            result.Succeeded
                ? $"Sentinel authenticated the encrypted container and restored a separate plaintext file.\n\nRestored file:\n{output}\n\nPlaintext bytes restored: {result.PlaintextBytes:N0}\n\nThe encrypted .sentinel.senc file remains unchanged."
                : $"Sentinel did not accept a plaintext output.\n\nStatus: {result.Code}\n{result.Message}\n\nInvalid plaintext output remains: {(result.InvalidOutputRemains ? "YES — review required" : "NO")}").ConfigureAwait(true);
    }

    private async Task EncryptExplorerFileAsync(string path, FrameworkElement rootElement)
    {
        string output = path + ".sentinel.senc";
        if (File.Exists(output) || Directory.Exists(output))
        {
            await ShowPrivacyMessageAsync(rootElement, "Encryption output already exists",
                $"This file has already produced an encrypted Sentinel container at:\n\n{output}\n\nSentinel will never overwrite that encrypted copy. If you intentionally want a new encrypted copy, rename or move the existing .sentinel.senc file first.");
            return;
        }

        FileInfo file = new(path);
        ContentDialog confirmation = new()
        {
            Title = "Encrypt File",
            Content = $"Selected file:\n{path}\n\nSize: {FormatBytes(file.Length)}\nProtection: AES-256-GCM encrypted Sentinel container\nKey protection: current Windows user\nEncrypted copy:\n{output}\n\nThe original plaintext file will remain unchanged. Encryption does not delete the original. If you later want the plaintext removed, use Secure Delete as a separate explicit action.",
            PrimaryButtonText = "Encrypt",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = rootElement.XamlRoot
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        using PremiumPrivacyEntitlementClient entitlement = new();
        PremiumPrivacyAuthorizationResult authorized = await entitlement.AuthorizeOneShotAsync(
            PremiumPrivacyEntitlementClient.EncryptScope).ConfigureAwait(true);
        if (!authorized.Succeeded)
        {
            await ShowEntitlementFailureAsync(rootElement, authorized).ConfigureAwait(true);
            return;
        }

        FileEncryptionService encryption = new();
        FileEncryptionResult result = await encryption.EncryptAsync(
            path,
            output,
            new IFileKeyProtector[] { new WindowsCurrentUserFileKeyProtector() }).ConfigureAwait(true);

        await ShowPrivacyMessageAsync(
            rootElement,
            result.Succeeded && result.Verified ? "Encrypted and verified" : "Encryption did not complete",
            result.Succeeded && result.Verified
                ? $"Encryption succeeded. Sentinel created, reopened, and authenticated this encrypted copy:\n\n{output}\n\nPlaintext bytes protected: {result.PlaintextBytes:N0}\n\nYour original file is still present and readable by design. To restore this encrypted copy later, use Decrypt on the .sentinel.senc file."
                : $"Sentinel did not report a verified encrypted output.\n\nStatus: {result.Code}\n{result.Message}\n\nInvalid output remains: {(result.InvalidOutputRemains ? "YES — review required" : "NO")}").ConfigureAwait(true);
    }

    private async Task AddExplorerFileToVaultAsync(string path, FrameworkElement rootElement)
    {
        string vaultRoot = Path.Combine(ApplicationData.Current.LocalFolder.Path, "SentinelVault");
        PersistedSentinelVaultService persistence = new(vaultRoot);
        FileInfo file = new(path);

        RecoveryKeyMaterial? newRecovery = null;
        try
        {
            if (!persistence.Exists)
            {
                newRecovery = RecoveryKeyMaterial.Generate();
                string recoveryText = newRecovery.ToDisplayString();
                ContentDialog recoveryDialog = new()
                {
                    Title = "Save your Sentinel Vault recovery key",
                    Content = $"Sentinel Vault is a private encrypted storage area managed by Sentinel. Files added to it are stored as encrypted Vault items inside Sentinel's app data instead of as ordinary readable copies.\n\nThis is the independent recovery key for your new Vault. Save it somewhere separate from this PC before continuing. Sentinel does not upload or retain the plaintext recovery key.\n\n{recoveryText}\n\nIf Windows account protection is later unavailable, this key is the recovery path to your Vault.",
                    PrimaryButtonText = "I saved it",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = rootElement.XamlRoot
                };
                if (await recoveryDialog.ShowAsync() != ContentDialogResult.Primary) return;
            }

            ContentDialog confirmation = new()
            {
                Title = "Add to Sentinel Vault",
                Content = $"Selected file:\n{path}\n\nSize: {FormatBytes(file.Length)}\n\nSentinel will create a verified encrypted Vault item inside Sentinel's private app storage. The selected source file remains in place unless you separately use Secure Delete.\n\nA full Vault browse/restore manager is being qualified separately; this action only adds a verified encrypted item.",
                PrimaryButtonText = "Add to Vault",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = rootElement.XamlRoot
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

            using PremiumPrivacyEntitlementClient entitlement = new();
            PremiumPrivacyAuthorizationResult authorized = await entitlement.AuthorizeOneShotAsync(
                PremiumPrivacyEntitlementClient.VaultScope).ConfigureAwait(true);
            if (!authorized.Succeeded)
            {
                await ShowEntitlementFailureAsync(rootElement, authorized).ConfigureAwait(true);
                return;
            }

            PersistedVaultSessionResult open = persistence.Exists
                ? await persistence.OpenCurrentUserAsync().ConfigureAwait(true)
                : await persistence.CreateAsync(newRecovery ?? throw new InvalidOperationException("New Vault recovery key was unavailable.")).ConfigureAwait(true);
            if (!open.Succeeded || open.Session is null)
            {
                await ShowPrivacyMessageAsync(rootElement, "Sentinel Vault could not open", open.Message + "\n\nStatus: " + open.Code).ConfigureAwait(true);
                return;
            }

            using PersistedVaultSession session = open.Session;
            VaultAddItemResult result = await session.Items.AddFileAsync(path).ConfigureAwait(true);
            await ShowPrivacyMessageAsync(
                rootElement,
                result.Succeeded ? "Added to Sentinel Vault" : "Vault item was not committed",
                result.Succeeded
                    ? $"Sentinel encrypted, verified, and committed the Vault item.\n\nItem ID: {result.ItemId:N}\nProtected bytes: {result.PlaintextBytes:N0}\n\nThe source file remains in place."
                    : $"Sentinel did not commit a Vault item.\n\nStatus: {result.Code}\nRecovery required: {(result.RecoveryRequired ? "YES" : "NO")}").ConfigureAwait(true);
        }
        finally
        {
            newRecovery?.Dispose();
        }
    }

    private async Task SecureDeleteExplorerFileAsync(string path, FrameworkElement rootElement)
    {
        SecureDeleteTargetValidationResult validation = SecureDeleteTargetValidator.Validate(path);
        if (!validation.Succeeded)
        {
            await ShowPrivacyMessageAsync(rootElement, "Secure Delete is not allowed for this file",
                validation.Message + "\n\nNo file was changed.").ConfigureAwait(true);
            return;
        }

        SecureDeleteCoordinator coordinator = new();
        SecureDeletePreparationResult prepared = coordinator.Prepare(validation.Target);
        if (!prepared.Succeeded || prepared.Authorization is null || prepared.Storage is null)
        {
            await ShowPrivacyMessageAsync(rootElement, "Secure Delete could not be prepared",
                prepared.Message + "\n\nNo file was changed.").ConfigureAwait(true);
            return;
        }

        StorageCapabilitySnapshot storage = prepared.Storage;
        ContentDialog confirmation = new()
        {
            Title = "Secure Delete",
            Content = $"Selected exact file:\n{prepared.Authorization.Target.CanonicalPath}\n\nSize: {FormatBytes(prepared.Authorization.Target.FileLength)}\nStorage: {storage.LocationKind} / {storage.PhysicalMedia}\nFile system: {storage.FileSystem ?? "unknown"}\nTRIM/UNMAP capability: {storage.TrimOrUnmapSupported}\nPremium entitlement: verified immediately before destructive execution\nRelated-copy discovery: exact-hash scan of the selected directory plus available supported providers\n\nSentinel will perform logical exact-object removal only. It does NOT claim SSD/NVMe physical-media erasure, and physical-media absence will be reported as CANNOT PROVE.\n\nThis action removes the selected file and cannot be undone through Sentinel.",
            PrimaryButtonText = "Secure Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = rootElement.XamlRoot
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        RelatedArtifactDiscoveryResult discovery;
        using (PremiumPrivacyEntitlementClient discoveryEntitlement = new())
        {
            PremiumPrivacyAuthorizationResult discoveryAuth = await discoveryEntitlement.AuthorizeOneShotAsync(
                PremiumPrivacyEntitlementClient.DiscoveryScope).ConfigureAwait(true);
            if (!discoveryAuth.Succeeded)
            {
                await ShowEntitlementFailureAsync(rootElement, discoveryAuth).ConfigureAwait(true);
                return;
            }

            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            RelatedArtifactDiscoveryService discoveryService = new();
            discovery = await discoveryService.DiscoverAsync(new RelatedArtifactDiscoveryRequest(
                path,
                string.IsNullOrWhiteSpace(directory) ? Array.Empty<string>() : new[] { directory },
                IncludeLocalOneDriveRoots: true)).ConfigureAwait(true);
            if (!discovery.Succeeded)
            {
                await ShowPrivacyMessageAsync(rootElement, "Related-copy discovery did not complete",
                    discovery.Summary + "\n\nSecure Delete did not proceed because the requested discovery phase was not source-qualified.").ConfigureAwait(true);
                return;
            }
        }

        using PremiumPrivacyEntitlementClient deleteEntitlement = new();
        PremiumPrivacyAuthorizationResult deleteAuth = await deleteEntitlement.AuthorizeOneShotAsync(
            PremiumPrivacyEntitlementClient.SecureDeleteScope).ConfigureAwait(true);
        if (!deleteAuth.Succeeded)
        {
            await ShowEntitlementFailureAsync(rootElement, deleteAuth).ConfigureAwait(true);
            return;
        }

        string journalRoot = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Privacy", "SecureDeleteJournal");
        SecureDeleteOperationJournal journal = new(journalRoot);
        SecureDeleteExactObjectExecutor executor = new(coordinator, journal);
        SecureDeleteExecutionResult result = executor.Execute(prepared.Authorization);

        int confirmed = discovery.Candidates.Count(c => c.Classification == RelatedArtifactClassification.ConfirmedCopy && !c.IsSameFilesystemObject);
        int likely = discovery.Candidates.Count(c => c.Classification == RelatedArtifactClassification.LikelyAttributableCopy);
        int metadata = discovery.Candidates.Count(c => c.Classification == RelatedArtifactClassification.MetadataReferenceOnly);
        int unverified = discovery.Candidates.Count(c => c.Classification == RelatedArtifactClassification.UnverifiedCandidate);
        int couldNotInspect = discovery.Providers.Count(p => p.State is RelatedArtifactProviderState.Unavailable or RelatedArtifactProviderState.Failed or RelatedArtifactProviderState.Limited);

        string primary = result.PrimaryRemoval switch
        {
            SecureDeletePrimaryRemovalStatus.Verified => "VERIFIED REMOVED",
            SecureDeletePrimaryRemovalStatus.Failed => "FAILED",
            _ => "UNKNOWN — RECOVERY REVIEW REQUIRED"
        };
        string media = result.MediaAction switch
        {
            SecureDeleteMediaActionStatus.Verified => "VERIFIED",
            SecureDeleteMediaActionStatus.Requested => "REQUESTED",
            SecureDeleteMediaActionStatus.NotSupported => "LOGICAL REMOVAL ONLY / MEDIA ACTION NOT SUPPORTED",
            _ => "CANNOT PROVE"
        };

        await ShowPrivacyMessageAsync(
            rootElement,
            result.Succeeded ? "Secure Delete primary removal finished" : "Secure Delete requires review",
            $"Primary file:\n{primary}\n\nStorage action:\n{media}\n\nRelated copies:\n{confirmed} confirmed\n{likely} likely\n{metadata} metadata references\n{unverified} unverified\n\nRemoved related copies:\n0\n\nRemaining related candidates:\n{confirmed + likely + metadata + unverified}\n\nCould not fully inspect providers:\n{couldNotInspect}\n\nPhysical-media absence:\nCANNOT PROVE\n\nJournal state: {result.JournalState?.ToString() ?? "none"}\n\nRelated candidates were discovered only; Sentinel did not reuse the primary file's deletion authority for them.").ConfigureAwait(true);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:N1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):N1} MB";
        return $"{bytes / (1024d * 1024 * 1024):N2} GB";
    }

    private static async Task ShowPrivacyMessageAsync(FrameworkElement rootElement, string title, string message)
    {
        await new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = rootElement.XamlRoot
        }.ShowAsync();
    }

    private static Task ShowEntitlementFailureAsync(FrameworkElement rootElement, PremiumPrivacyAuthorizationResult authorization) =>
        ShowPrivacyMessageAsync(rootElement,
            authorization.ServiceAvailable ? "Premium Privacy subscription required" : "Premium Privacy verification unavailable",
            authorization.Message + "\n\nStatus: " + authorization.Code +
            "\n\nExisting encrypted files and Vault recovery remain accessible through their recovery/decryption paths; Sentinel is only blocking a new premium creation or cleanup action.");
}
