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
    private const int PortableEncryptionMinimumPasswordLength = 12;
    private const int PortableEncryptionMaximumPasswordLength = 256;

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
            case ExplorerRequestedAction.EncryptForSharing:
                await EncryptExplorerFileForSharingAsync(path, rootElement).ConfigureAwait(true);
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

        // Bind the encrypted source before decryption. If it changes while recovery is in
        // progress, the exact-object retirement gate will fail closed instead of deleting a
        // replacement object that merely appears at the same path.
        SecureDeleteTargetValidationResult encryptedSourceIdentity = SecureDeleteTargetValidator.Validate(path);

        ContentDialog confirmation = new()
        {
            Title = "Decrypt Sentinel File",
            Content = $"Encrypted container:\n{path}\n\nRestored plaintext output:\n{output}\n\nSentinel will authenticate the encrypted container before accepting the restored file. After a successful verified restore, Sentinel will retire the exact encrypted source object. If source retirement cannot be proved safe, the restored plaintext is kept and the encrypted source remains for review. Decryption of your existing Sentinel data does not require a current subscription.\n\nFor files protected for this PC, Sentinel will first try the current Windows user. For portable shared files, Sentinel will ask for the sharing password if needed.",
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

        while (!result.Succeeded &&
               string.Equals(result.Code, "KeyUnavailable", StringComparison.Ordinal) &&
               !result.InvalidOutputRemains &&
               !File.Exists(output) &&
               !Directory.Exists(output))
        {
            char[]? password = await PromptForPortablePasswordAsync(
                rootElement,
                "Password required",
                "This Sentinel file was not unlocked by the current Windows user. If it was encrypted for sharing, enter the password supplied by the sender. If the password is not accepted, Sentinel will let you try again. Sentinel does not store or recover this password.",
                requireConfirmation: false,
                enforceCreationPolicy: false).ConfigureAwait(true);

            if (password is null) return;
            try
            {
                using PasswordFileKeyProtector passwordProtector = new(password);
                Array.Clear(password, 0, password.Length);
                result = await encryption.DecryptAsync(
                    path,
                    output,
                    new IFileKeyProtector[] { passwordProtector }).ConfigureAwait(true);
            }
            finally
            {
                Array.Clear(password, 0, password.Length);
            }

            if (!result.Succeeded &&
                string.Equals(result.Code, "KeyUnavailable", StringComparison.Ordinal) &&
                !result.InvalidOutputRemains &&
                !File.Exists(output) &&
                !Directory.Exists(output))
            {
                await ShowPrivacyMessageAsync(rootElement, "Password not accepted",
                    "Sentinel could not unlock this portable encrypted file with that password. The encrypted file was not changed. You can try the password again or cancel.").ConfigureAwait(true);
            }
        }

        if (!result.Succeeded)
        {
            await ShowPrivacyMessageAsync(
                rootElement,
                "Decryption did not complete",
                string.Equals(result.Code, "KeyUnavailable", StringComparison.Ordinal)
                    ? $"Sentinel could not unlock this encrypted file with the supplied Windows identity or password. No plaintext output was accepted.\n\nStatus: {result.Code}\n\nIf this is a shared file, verify the password with the sender using a separate communication channel."
                    : $"Sentinel did not accept a plaintext output.\n\nStatus: {result.Code}\n{result.Message}\n\nInvalid plaintext output remains: {(result.InvalidOutputRemains ? "YES — review required" : "NO")}").ConfigureAwait(true);
            return;
        }

        bool encryptedSourceRemoved = false;
        string retirementDetail;
        if (!encryptedSourceIdentity.Succeeded)
        {
            retirementDetail = "Sentinel restored and authenticated the plaintext, but it could not safely bind the encrypted source to an exact filesystem identity before recovery. The encrypted .sentinel.senc file was therefore kept.";
        }
        else
        {
            SecureDeleteCoordinator coordinator = new();
            SecureDeletePreparationResult prepared = coordinator.Prepare(encryptedSourceIdentity.Target);
            if (!prepared.Succeeded || prepared.Authorization is null)
            {
                retirementDetail = "Sentinel restored and authenticated the plaintext, but the exact encrypted source could not be approved for safe logical retirement. The encrypted .sentinel.senc file was kept. " + prepared.Message;
            }
            else
            {
                string journalRoot = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Privacy", "SecureDeleteJournal");
                SecureDeleteOperationJournal journal = new(journalRoot);
                SecureDeleteExactObjectExecutor executor = new(coordinator, journal);
                SecureDeleteExecutionResult retirement = executor.Execute(prepared.Authorization);
                encryptedSourceRemoved = retirement.Succeeded && retirement.PrimaryRemoval == SecureDeletePrimaryRemovalStatus.Verified;
                retirementDetail = encryptedSourceRemoved
                    ? "The exact encrypted source object was logically removed and its absence was verified. Sentinel does not claim that SSD/NVMe physical-media remnants are provably erased."
                    : $"The plaintext restore succeeded, but Sentinel could not prove safe removal of the encrypted source. The .sentinel.senc file remains for review. Status: {retirement.Code}. {retirement.Message}";
            }
        }

        await ShowPrivacyMessageAsync(
            rootElement,
            encryptedSourceRemoved ? "Decrypted, verified, and source retired" : "Decrypted and verified — encrypted source kept",
            $"Sentinel authenticated the encrypted container and restored the plaintext file.\n\nRestored file:\n{output}\n\nPlaintext bytes restored: {result.PlaintextBytes:N0}\n\n{retirementDetail}").ConfigureAwait(true);
    }

    private async Task EncryptExplorerFileAsync(string path, FrameworkElement rootElement)
    {
        string output = path + ".sentinel.senc";
        if (File.Exists(output) || Directory.Exists(output))
        {
            await ShowPrivacyMessageAsync(rootElement, "Encryption output already exists",
                $"This file already has a Sentinel encrypted file at:\n\n{output}\n\nSentinel will never overwrite it. Move or rename the existing .sentinel.senc file first if you intentionally need to encrypt this plaintext file again.");
            return;
        }

        FileInfo file = new(path);
        ContentDialog confirmation = new()
        {
            Title = "Encrypt for This PC",
            Content = $"Selected file:\n{path}\n\nSize: {FormatBytes(file.Length)}\nProtection: AES-256-GCM encrypted Sentinel container\nKey protection: current Windows user\nEncrypted file:\n{output}\n\nThis mode is convenient protection against offline access, copied files, and other Windows profiles. It is not intended to protect plaintext from someone who already controls your unlocked Windows user session. Use Encrypt for Sharing when you need a password-protected file that is independent of this Windows profile.\n\nSentinel will first create, flush, reopen, and authenticate the encrypted file. Only after that succeeds will Sentinel remove the exact readable plaintext source automatically. If encryption or verification fails, the original remains. This removes the ordinary plaintext file but does not claim that storage-media remnants are physically unrecoverable.",
            PrimaryButtonText = "Encrypt for This PC",
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

        FileEncryptionReplacementService replacement = new();
        FileEncryptionResult result = await replacement.EncryptReplacingSourceAsync(
            path,
            output,
            new IFileKeyProtector[] { new WindowsCurrentUserFileKeyProtector() }).ConfigureAwait(true);

        await ShowPrivacyMessageAsync(
            rootElement,
            result.Succeeded && result.Verified ? "Encrypted and verified" : "Encryption did not complete",
            result.Succeeded && result.Verified
                ? $"Sentinel created, reopened, and authenticated the encrypted file, then removed the readable plaintext source.\n\nEncrypted file:\n{output}\n\nPlaintext bytes protected: {result.PlaintextBytes:N0}\n\nTo restore the file later, use Decrypt on the .sentinel.senc file."
                : $"Sentinel did not complete the verified replacement operation.\n\nStatus: {result.Code}\n{result.Message}\n\nEncrypted output verification: {(result.Verified ? "VERIFIED" : "NOT VERIFIED")}\nInvalid partial output remains: {(result.InvalidOutputRemains ? "YES — review required" : "NO")}").ConfigureAwait(true);
    }

    private async Task EncryptExplorerFileForSharingAsync(string path, FrameworkElement rootElement)
    {
        string output = path + ".sentinel.senc";
        if (File.Exists(output) || Directory.Exists(output))
        {
            await ShowPrivacyMessageAsync(rootElement, "Encryption output already exists",
                $"Sentinel will not overwrite this existing encrypted file:\n\n{output}\n\nMove or rename the existing .sentinel.senc file before encrypting this plaintext file again.").ConfigureAwait(true);
            return;
        }

        FileInfo file = new(path);
        ContentDialog confirmation = new()
        {
            Title = "Encrypt for Sharing",
            Content = $"Selected file:\n{path}\n\nSize: {FormatBytes(file.Length)}\nPortable encrypted file:\n{output}\n\nSentinel will protect this file with a password-derived key using Argon2id and authenticated AES-256-GCM encryption. The encrypted file will not depend on your Windows user profile, so it can be sent to another Sentinel user and decrypted there with the password.\n\nSend the password separately from the encrypted file. Sentinel does not store or recover the password.\n\nSentinel will remove the readable plaintext source automatically only after the portable encrypted file has been flushed, reopened, and authenticated successfully. If encryption or verification fails, the original remains. This is normal exact-file removal and is not a claim of physical-media erasure.",
            PrimaryButtonText = "Continue",
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

        char[]? password = await PromptForPortablePasswordAsync(
            rootElement,
            "Create sharing password",
            $"Choose a password of at least {PortableEncryptionMinimumPasswordLength} characters. Enter it twice. If it is too short or the two entries do not match, Sentinel will keep the password dialog open for another try. The recipient must know this exact password to decrypt the file. Share it through a different communication channel than the encrypted file.",
            requireConfirmation: true,
            enforceCreationPolicy: true).ConfigureAwait(true);
        if (password is null) return;

        FileEncryptionResult result;
        try
        {
            using PasswordFileKeyProtector protector = new(password);
            Array.Clear(password, 0, password.Length);
            FileEncryptionReplacementService replacement = new();
            result = await replacement.EncryptReplacingSourceAsync(
                path,
                output,
                new IFileKeyProtector[] { protector }).ConfigureAwait(true);
        }
        finally
        {
            Array.Clear(password, 0, password.Length);
        }

        await ShowPrivacyMessageAsync(
            rootElement,
            result.Succeeded && result.Verified ? "Portable encrypted file verified" : "Encryption did not complete",
            result.Succeeded && result.Verified
                ? $"Sentinel created, reopened, and authenticated the portable encrypted file, then removed the readable plaintext source.\n\nEncrypted file:\n{output}\n\nPlaintext bytes protected: {result.PlaintextBytes:N0}\n\nThis .sentinel.senc file can be sent to another Sentinel user. They will need the sharing password to decrypt it. Send that password separately."
                : $"Sentinel did not complete the portable encryption replacement.\n\nStatus: {result.Code}\n{result.Message}\n\nEncrypted output verification: {(result.Verified ? "VERIFIED" : "NOT VERIFIED")}\nInvalid partial output remains: {(result.InvalidOutputRemains ? "YES — review required" : "NO")}").ConfigureAwait(true);
    }

    private static async Task<char[]?> PromptForPortablePasswordAsync(
        FrameworkElement rootElement,
        string title,
        string instructions,
        bool requireConfirmation,
        bool enforceCreationPolicy)
    {
        while (true)
        {
            PasswordBox passwordBox = new()
            {
                Header = "Password",
                MaxLength = PortableEncryptionMaximumPasswordLength,
                PlaceholderText = requireConfirmation ? "Create a strong password" : "Enter sharing password"
            };
            PasswordBox? confirmationBox = requireConfirmation
                ? new PasswordBox
                {
                    Header = "Confirm password",
                    MaxLength = PortableEncryptionMaximumPasswordLength,
                    PlaceholderText = "Enter the same password again"
                }
                : null;

            StackPanel panel = new() { Spacing = 10, MinWidth = 420 };
            panel.Children.Add(new TextBlock { Text = instructions, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(passwordBox);
            if (confirmationBox is not null) panel.Children.Add(confirmationBox);

            ContentDialog dialog = new()
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = requireConfirmation ? "Encrypt" : "Unlock",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = rootElement.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                passwordBox.Password = string.Empty;
                if (confirmationBox is not null) confirmationBox.Password = string.Empty;
                return null;
            }

            char[] password = passwordBox.Password.ToCharArray();
            char[] confirmation = confirmationBox?.Password.ToCharArray() ?? Array.Empty<char>();
            passwordBox.Password = string.Empty;
            if (confirmationBox is not null) confirmationBox.Password = string.Empty;

            try
            {
                if (password.Length == 0)
                {
                    Array.Clear(password, 0, password.Length);
                    await ShowPrivacyMessageAsync(rootElement, "Password required", "No password was entered. Enter a password to continue, or choose Cancel to leave the file unchanged.").ConfigureAwait(true);
                    continue;
                }

                if (enforceCreationPolicy && password.Length < PortableEncryptionMinimumPasswordLength)
                {
                    Array.Clear(password, 0, password.Length);
                    await ShowPrivacyMessageAsync(rootElement, "Password is too short",
                        $"Use at least {PortableEncryptionMinimumPasswordLength} characters for a portable encrypted file. The password entry will open again so you can try another password.").ConfigureAwait(true);
                    continue;
                }

                if (requireConfirmation && !password.AsSpan().SequenceEqual(confirmation))
                {
                    Array.Clear(password, 0, password.Length);
                    await ShowPrivacyMessageAsync(rootElement, "Passwords do not match", "The two passwords were different. The password entry will open again so you can re-enter both values.").ConfigureAwait(true);
                    continue;
                }

                return password;
            }
            finally
            {
                Array.Clear(confirmation, 0, confirmation.Length);
            }
        }
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

                StackPanel recoveryPanel = new() { Spacing = 10, MinWidth = 480 };
                recoveryPanel.Children.Add(new TextBlock
                {
                    Text = "Sentinel Vault is a private encrypted storage area managed by Sentinel. Files added to it are stored as encrypted Vault items inside Sentinel's app data instead of as ordinary readable copies.\n\nThis is the independent recovery key for your new Vault. Copy it and save it somewhere separate from this PC before continuing. Sentinel does not upload or retain the plaintext recovery key.",
                    TextWrapping = TextWrapping.Wrap
                });
                recoveryPanel.Children.Add(new TextBox
                {
                    Header = "Vault recovery key — selectable and copyable",
                    Text = recoveryText,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    IsSpellCheckEnabled = false
                });
                recoveryPanel.Children.Add(new TextBlock
                {
                    Text = "Use Ctrl+A then Ctrl+C in the recovery-key box, or select the key and copy it. If Windows account protection is later unavailable, this key is the recovery path to your Vault.",
                    TextWrapping = TextWrapping.Wrap
                });

                ContentDialog recoveryDialog = new()
                {
                    Title = "Save your Sentinel Vault recovery key",
                    Content = recoveryPanel,
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
