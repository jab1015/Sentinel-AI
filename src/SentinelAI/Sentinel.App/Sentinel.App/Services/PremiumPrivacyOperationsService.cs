using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

/// <summary>
/// Server-authoritative application boundary for Premium Privacy creation/cleanup features.
/// Explorer/UI code must enter through this service rather than treating UI state or a local
/// subscription bool as authorization. Decryption/recovery of existing user-owned encrypted
/// data is intentionally not exposed through this premium gate.
/// </summary>
internal sealed class PremiumPrivacyOperationsService
{
    private readonly PremiumPrivacyEntitlementClient _entitlement;
    private readonly FileEncryptionService _encryption;
    private readonly VaultItemStoreService _vaultItems;
    private readonly SecureDeleteExactObjectExecutor _secureDelete;
    private readonly RelatedArtifactDiscoveryService _discovery;

    internal PremiumPrivacyOperationsService(
        PremiumPrivacyEntitlementClient entitlement,
        FileEncryptionService encryption,
        VaultItemStoreService vaultItems,
        SecureDeleteExactObjectExecutor secureDelete,
        RelatedArtifactDiscoveryService discovery)
    {
        _entitlement = entitlement ?? throw new ArgumentNullException(nameof(entitlement));
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _vaultItems = vaultItems ?? throw new ArgumentNullException(nameof(vaultItems));
        _secureDelete = secureDelete ?? throw new ArgumentNullException(nameof(secureDelete));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
    }

    internal async Task<PremiumPrivacyOperationResult<FileEncryptionResult>> EncryptFileAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyList<IFileKeyProtector> keyProtectors,
        CancellationToken cancellationToken = default)
    {
        PremiumPrivacyAuthorizationResult authorization = await _entitlement.AuthorizeOneShotAsync(
            PremiumPrivacyEntitlementClient.EncryptScope,
            cancellationToken).ConfigureAwait(false);
        if (!authorization.Succeeded)
            return PremiumPrivacyOperationResult<FileEncryptionResult>.Denied(authorization);

        FileEncryptionResult result = await _encryption.EncryptAsync(
            sourcePath,
            outputPath,
            keyProtectors,
            cancellationToken).ConfigureAwait(false);
        return PremiumPrivacyOperationResult<FileEncryptionResult>.Completed(authorization, result);
    }

    internal async Task<PremiumPrivacyOperationResult<VaultAddItemResult>> AddToVaultAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        PremiumPrivacyAuthorizationResult authorization = await _entitlement.AuthorizeOneShotAsync(
            PremiumPrivacyEntitlementClient.VaultScope,
            cancellationToken).ConfigureAwait(false);
        if (!authorization.Succeeded)
            return PremiumPrivacyOperationResult<VaultAddItemResult>.Denied(authorization);

        VaultAddItemResult result = await _vaultItems.AddFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return PremiumPrivacyOperationResult<VaultAddItemResult>.Completed(authorization, result);
    }

    internal async Task<PremiumPrivacyOperationResult<SecureDeleteExecutionResult>> SecureDeleteAsync(
        SecureDeleteAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        PremiumPrivacyAuthorizationResult entitlement = await _entitlement.AuthorizeOneShotAsync(
            PremiumPrivacyEntitlementClient.SecureDeleteScope,
            cancellationToken).ConfigureAwait(false);
        if (!entitlement.Succeeded)
            return PremiumPrivacyOperationResult<SecureDeleteExecutionResult>.Denied(entitlement);

        cancellationToken.ThrowIfCancellationRequested();
        SecureDeleteExecutionResult result = _secureDelete.Execute(authorization);
        return PremiumPrivacyOperationResult<SecureDeleteExecutionResult>.Completed(entitlement, result);
    }

    internal async Task<PremiumPrivacyOperationResult<RelatedArtifactDiscoveryResult>> DiscoverRelatedAsync(
        RelatedArtifactDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PremiumPrivacyAuthorizationResult authorization = await _entitlement.AuthorizeOneShotAsync(
            PremiumPrivacyEntitlementClient.DiscoveryScope,
            cancellationToken).ConfigureAwait(false);
        if (!authorization.Succeeded)
            return PremiumPrivacyOperationResult<RelatedArtifactDiscoveryResult>.Denied(authorization);

        RelatedArtifactDiscoveryResult result = await _discovery.DiscoverAsync(request, cancellationToken).ConfigureAwait(false);
        return PremiumPrivacyOperationResult<RelatedArtifactDiscoveryResult>.Completed(authorization, result);
    }
}

internal sealed record PremiumPrivacyOperationResult<T>(
    bool Authorized,
    PremiumPrivacyAuthorizationResult Entitlement,
    T? Result)
    where T : class
{
    internal static PremiumPrivacyOperationResult<T> Denied(PremiumPrivacyAuthorizationResult entitlement) =>
        new(false, entitlement, null);

    internal static PremiumPrivacyOperationResult<T> Completed(PremiumPrivacyAuthorizationResult entitlement, T result) =>
        new(true, entitlement, result);
}
