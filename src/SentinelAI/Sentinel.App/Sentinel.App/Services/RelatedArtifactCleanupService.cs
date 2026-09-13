using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal enum RelatedArtifactCleanupStatus
{
    VerifiedRemoved = 0,
    Rejected,
    EntitlementUnavailable,
    Failed,
    Unknown,
    NotSupported
}

internal sealed record RelatedArtifactCleanupResult(
    RelatedArtifactCleanupStatus Status,
    string Code,
    string Message,
    Guid OperationId,
    SecureDeletePrimaryRemovalStatus? PrimaryRemoval)
{
    internal bool Succeeded => Status == RelatedArtifactCleanupStatus.VerifiedRemoved;
}

/// <summary>
/// Converts a discovery candidate into a completely new exact-target operation. Discovery
/// evidence is never mutation authority and the primary file authorization is never reused.
/// </summary>
internal sealed class RelatedArtifactCleanupService
{
    private readonly PremiumPrivacyEntitlementClient _entitlement;
    private readonly SecureDeleteOperationJournal _journal;

    internal RelatedArtifactCleanupService(
        PremiumPrivacyEntitlementClient entitlement,
        SecureDeleteOperationJournal journal)
    {
        _entitlement = entitlement ?? throw new ArgumentNullException(nameof(entitlement));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

    internal async Task<RelatedArtifactCleanupResult> RemoveFilesystemCandidateAsync(
        RelatedArtifactCandidate candidate,
        bool explicitUserConfirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.IsSameFilesystemObject)
            return Reject("SameFilesystemObject", "A hardlink/path to the same filesystem object is not an independent related-copy deletion target.");

        if (candidate.Classification == RelatedArtifactClassification.UnverifiedCandidate)
            return Reject("UnverifiedCandidate", "Unverified discovery candidates can never be deleted through related cleanup.");

        if (candidate.Classification == RelatedArtifactClassification.MetadataReferenceOnly)
            return new(RelatedArtifactCleanupStatus.NotSupported, "MetadataProviderRequired",
                "Metadata references require a provider-specific targeted cleanup operation; filesystem deletion is not authorized.",
                Guid.Empty, null);

        if (candidate.Classification == RelatedArtifactClassification.LikelyAttributableCopy && !explicitUserConfirmation)
            return Reject("ConfirmationRequired", "A likely attributable copy requires explicit user confirmation before any exact-target validation begins.");

        if (candidate.Classification == RelatedArtifactClassification.ConfirmedCopy && !explicitUserConfirmation)
            return Reject("ConfirmationRequired", "Sentinel requires explicit confirmation before deleting a discovered related copy.");

        if (string.IsNullOrWhiteSpace(candidate.Location) || !Path.IsPathFullyQualified(candidate.Location))
            return Reject("InvalidLocation", "The related candidate is not a fully-qualified filesystem object.");

        cancellationToken.ThrowIfCancellationRequested();
        SecureDeleteTargetValidationResult validation = SecureDeleteTargetValidator.Validate(candidate.Location);
        if (!validation.Succeeded)
            return Reject("TargetRejected", "The related candidate failed fresh exact-target validation: " + validation.Message);

        SecureDeleteCoordinator coordinator = new();
        SecureDeletePreparationResult prepared = coordinator.Prepare(validation.Target);
        if (!prepared.Succeeded || prepared.Authorization is null)
            return Reject("PreparationRejected", prepared.Message);

        PremiumPrivacyAuthorizationResult entitlement = await _entitlement.AuthorizeOneShotAsync(
            PremiumPrivacyEntitlementClient.SecureDeleteScope,
            cancellationToken).ConfigureAwait(false);
        if (!entitlement.Succeeded)
        {
            return new(
                entitlement.ServiceAvailable ? RelatedArtifactCleanupStatus.Rejected : RelatedArtifactCleanupStatus.EntitlementUnavailable,
                entitlement.Code,
                entitlement.Message,
                Guid.Empty,
                null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SecureDeleteExactObjectExecutor executor = new(coordinator, _journal);
        SecureDeleteExecutionResult execution = executor.Execute(prepared.Authorization);
        RelatedArtifactCleanupStatus status = execution.PrimaryRemoval switch
        {
            SecureDeletePrimaryRemovalStatus.Verified when execution.Succeeded => RelatedArtifactCleanupStatus.VerifiedRemoved,
            SecureDeletePrimaryRemovalStatus.Unknown => RelatedArtifactCleanupStatus.Unknown,
            _ => RelatedArtifactCleanupStatus.Failed
        };

        return new(
            status,
            execution.Code,
            execution.Message,
            execution.OperationId,
            execution.PrimaryRemoval);
    }

    /// <summary>
    /// Marks the primary transaction complete only after the application has resolved the
    /// related-artifact phase (removed, explicitly retained, or documented as unsupported).
    /// This method performs no deletion itself.
    /// </summary>
    internal SecureDeleteOperationRecord CompleteRelatedPhase(Guid primaryOperationId, string resolutionSummary)
    {
        if (primaryOperationId == Guid.Empty)
            throw new ArgumentException("A primary Secure Delete operation ID is required.", nameof(primaryOperationId));
        if (string.IsNullOrWhiteSpace(resolutionSummary))
            throw new ArgumentException("A related-cleanup resolution summary is required.", nameof(resolutionSummary));

        SecureDeleteOperationRecord current = _journal.ReadRequired(primaryOperationId);
        if (current.State != SecureDeleteOperationState.RelatedCleanupPending)
            throw new InvalidOperationException("Only a verified primary operation awaiting related cleanup can be completed.");

        return _journal.Advance(
            current,
            SecureDeleteOperationState.Complete,
            "Related-artifact phase resolved: " + resolutionSummary.Trim());
    }

    private static RelatedArtifactCleanupResult Reject(string code, string message) =>
        new(RelatedArtifactCleanupStatus.Rejected, code, message, Guid.Empty, null);
}
