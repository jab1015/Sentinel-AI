using System;

namespace Sentinel.App.Services;

/// <summary>
/// Non-destructive authorization boundary for Secure Delete. This coordinator deliberately
/// exposes no path-based delete/overwrite primitive. It accepts only an identity that was
/// previously bound by <see cref="SecureDeleteTargetValidator"/>, revalidates that exact
/// object, snapshots conservative storage facts, and issues a short-lived authorization.
/// The authorization must be revalidated again immediately before any future mutation.
/// </summary>
internal sealed class SecureDeleteCoordinator
{
    internal static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(1);

    private readonly Func<DateTimeOffset> _utcNow;

    internal SecureDeleteCoordinator(Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ?? static () => DateTimeOffset.UtcNow;
    }

    internal SecureDeletePreparationResult Prepare(SecureDeleteTargetIdentity approvedTarget)
    {
        if (approvedTarget.IsEmpty)
            return SecureDeletePreparationResult.Fail(
                SecureDeleteCoordinatorCode.InvalidTarget,
                "Secure Delete preparation requires a previously validated exact target identity.");

        SecureDeleteTargetValidationResult validation = SecureDeleteTargetValidator.Revalidate(approvedTarget);
        if (!validation.Succeeded)
            return SecureDeletePreparationResult.Fail(
                MapValidationFailure(validation.Code),
                "The approved target could not be revalidated for Secure Delete: " + validation.Message);

        StorageCapabilitySnapshot storage = StorageCapabilityDetector.Detect(validation.Target);
        if (storage.LocationKind != StorageLocationKind.LocalFixed || string.IsNullOrWhiteSpace(storage.VolumeRoot))
            return SecureDeletePreparationResult.Fail(
                SecureDeleteCoordinatorCode.UnsupportedStorageLocation,
                "Secure Delete mutation is currently limited to validated files on local fixed storage.",
                storage);

        DateTimeOffset created = _utcNow();
        SecureDeleteAuthorization authorization = new(
            Guid.NewGuid(),
            created,
            created + AuthorizationLifetime,
            validation.Target,
            storage.VolumeRoot,
            storage.FileSystem,
            storage.LocationKind,
            storage.PhysicalMedia,
            AllowsLogicalRemoval: true,
            AllowsOverwriteSanitization: false);

        return SecureDeletePreparationResult.Success(authorization, storage);
    }

    internal SecureDeleteMutationGateResult RevalidateForMutation(SecureDeleteAuthorization? authorization)
    {
        if (authorization is null || authorization.AuthorizationId == Guid.Empty || authorization.Target.IsEmpty)
            return SecureDeleteMutationGateResult.Fail(
                SecureDeleteCoordinatorCode.InvalidAuthorization,
                "No valid Secure Delete authorization was supplied.");

        DateTimeOffset now = _utcNow();
        if (authorization.CreatedUtc > now + MaximumClockSkew ||
            authorization.ExpiresUtc <= authorization.CreatedUtc ||
            now > authorization.ExpiresUtc)
        {
            return SecureDeleteMutationGateResult.Fail(
                SecureDeleteCoordinatorCode.AuthorizationExpired,
                "The Secure Delete authorization expired or had invalid timing metadata.");
        }

        SecureDeleteTargetValidationResult validation = SecureDeleteTargetValidator.Revalidate(authorization.Target);
        if (!validation.Succeeded)
            return SecureDeleteMutationGateResult.Fail(
                MapValidationFailure(validation.Code),
                "The approved filesystem object changed before Secure Delete mutation: " + validation.Message);

        StorageCapabilitySnapshot storage = StorageCapabilityDetector.Detect(validation.Target);
        if (storage.LocationKind != StorageLocationKind.LocalFixed || string.IsNullOrWhiteSpace(storage.VolumeRoot))
            return SecureDeleteMutationGateResult.Fail(
                SecureDeleteCoordinatorCode.UnsupportedStorageLocation,
                "The target is no longer on a supported local fixed-storage boundary.",
                storage);

        if (!storage.VolumeRoot.Equals(authorization.VolumeRoot, StringComparison.OrdinalIgnoreCase) ||
            storage.LocationKind != authorization.LocationKind ||
            !string.Equals(storage.FileSystem, authorization.FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            return SecureDeleteMutationGateResult.Fail(
                SecureDeleteCoordinatorCode.StorageBoundaryChanged,
                "Storage identity/capability facts changed after Secure Delete approval.",
                storage);
        }

        if (!authorization.AllowsLogicalRemoval || authorization.AllowsOverwriteSanitization)
            return SecureDeleteMutationGateResult.Fail(
                SecureDeleteCoordinatorCode.InvalidAuthorization,
                "The Secure Delete authorization contained unsupported mutation privileges.",
                storage);

        return SecureDeleteMutationGateResult.Ready(validation.Target, storage);
    }

    private static SecureDeleteCoordinatorCode MapValidationFailure(SecureDeleteTargetValidationCode code) =>
        code == SecureDeleteTargetValidationCode.IdentityChanged ||
        code == SecureDeleteTargetValidationCode.MissingOrInaccessible
            ? SecureDeleteCoordinatorCode.IdentityChanged
            : SecureDeleteCoordinatorCode.TargetRejected;
}

internal enum SecureDeleteCoordinatorCode
{
    Prepared = 0,
    MutationGateReady,
    InvalidTarget,
    InvalidAuthorization,
    AuthorizationExpired,
    TargetRejected,
    IdentityChanged,
    UnsupportedStorageLocation,
    StorageBoundaryChanged
}

internal sealed record SecureDeleteAuthorization(
    Guid AuthorizationId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    SecureDeleteTargetIdentity Target,
    string VolumeRoot,
    string? FileSystem,
    StorageLocationKind LocationKind,
    PhysicalMediaKind PhysicalMedia,
    bool AllowsLogicalRemoval,
    bool AllowsOverwriteSanitization);

internal sealed record SecureDeletePreparationResult(
    bool Succeeded,
    SecureDeleteCoordinatorCode Code,
    string Message,
    SecureDeleteAuthorization? Authorization,
    StorageCapabilitySnapshot? Storage)
{
    internal static SecureDeletePreparationResult Success(
        SecureDeleteAuthorization authorization,
        StorageCapabilitySnapshot storage) =>
        new(true, SecureDeleteCoordinatorCode.Prepared,
            "Secure Delete exact-target authorization prepared; no destructive mutation has occurred.",
            authorization, storage);

    internal static SecureDeletePreparationResult Fail(
        SecureDeleteCoordinatorCode code,
        string message,
        StorageCapabilitySnapshot? storage = null) =>
        new(false, code, message, null, storage);
}

internal sealed record SecureDeleteMutationGateResult(
    bool Succeeded,
    SecureDeleteCoordinatorCode Code,
    string Message,
    SecureDeleteTargetIdentity Target,
    StorageCapabilitySnapshot? Storage)
{
    internal static SecureDeleteMutationGateResult Ready(
        SecureDeleteTargetIdentity target,
        StorageCapabilitySnapshot storage) =>
        new(true, SecureDeleteCoordinatorCode.MutationGateReady,
            "Secure Delete exact target and storage boundary revalidated for a future narrow mutation operation.",
            target, storage);

    internal static SecureDeleteMutationGateResult Fail(
        SecureDeleteCoordinatorCode code,
        string message,
        StorageCapabilitySnapshot? storage = null) =>
        new(false, code, message, default, storage);
}
