using System;

namespace Sentinel.App.Services;

internal enum SecureDeleteRecoveryPhase
{
    JournalInvalid = 0,
    PreMutation,
    MutationStarted,
    RemovalRecorded,
    RelatedCleanupPending,
    Complete,
    RecoveryRequired
}

internal enum SecureDeleteRecoveryTargetObservation
{
    Unknown = 0,
    ExactOriginalPresent,
    DifferentObjectPresent,
    MissingOrInaccessible,
    RejectedOrUnverified
}

internal sealed record SecureDeleteRecoveryAssessment(
    SecureDeleteRecoveryPhase Phase,
    SecureDeleteRecoveryTargetObservation TargetObservation,
    SecureDeleteOperationRecord? Record,
    bool RequiresUserAttention,
    bool MayRequestFreshPreMutationAuthorization,
    bool MayAutomaticallyResumeMutation,
    string Message);

/// <summary>
/// Read-only crash/restart classification for Secure Delete. This component never mutates
/// the target or journal. It authenticates the persisted operation through the journal,
/// then compares current filesystem evidence with the original stable identity. Ambiguous,
/// contradictory, replaced, missing, or rejected states remain fail-closed and never grant
/// automatic mutation authority.
/// </summary>
internal sealed class SecureDeleteRecoveryClassifier
{
    private readonly SecureDeleteOperationJournal _journal;

    internal SecureDeleteRecoveryClassifier(SecureDeleteOperationJournal journal)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

    internal SecureDeleteRecoveryAssessment Assess(Guid operationId)
    {
        if (!_journal.TryRead(operationId, out SecureDeleteOperationRecord? record) || record is null)
        {
            return new(
                SecureDeleteRecoveryPhase.JournalInvalid,
                SecureDeleteRecoveryTargetObservation.Unknown,
                null,
                RequiresUserAttention: true,
                MayRequestFreshPreMutationAuthorization: false,
                MayAutomaticallyResumeMutation: false,
                "Secure Delete recovery cannot trust the operation journal.");
        }

        SecureDeleteRecoveryTargetObservation observation = ObserveTarget(record.Target);
        SecureDeleteRecoveryPhase phase = MapPhase(record.State);

        if (phase == SecureDeleteRecoveryPhase.PreMutation)
        {
            if (observation == SecureDeleteRecoveryTargetObservation.ExactOriginalPresent)
            {
                return new(
                    phase,
                    observation,
                    record,
                    RequiresUserAttention: false,
                    MayRequestFreshPreMutationAuthorization: true,
                    MayAutomaticallyResumeMutation: false,
                    "The original approved file is still present and unchanged. Any future mutation requires fresh authorization.");
            }

            return new(
                phase,
                observation,
                record,
                RequiresUserAttention: true,
                MayRequestFreshPreMutationAuthorization: false,
                MayAutomaticallyResumeMutation: false,
                "Pre-mutation recovery evidence no longer proves the originally approved object is safely available.");
        }

        if (phase == SecureDeleteRecoveryPhase.MutationStarted)
        {
            return new(
                phase,
                observation,
                record,
                RequiresUserAttention: true,
                MayRequestFreshPreMutationAuthorization: false,
                MayAutomaticallyResumeMutation: false,
                observation == SecureDeleteRecoveryTargetObservation.ExactOriginalPresent
                    ? "A mutation-started record exists while the original file is still present. The prior operation is ambiguous and must not auto-resume."
                    : "A mutation-started record exists but current filesystem evidence cannot prove a safe automatic continuation.");
        }

        if (phase is SecureDeleteRecoveryPhase.RemovalRecorded or
            SecureDeleteRecoveryPhase.RelatedCleanupPending or
            SecureDeleteRecoveryPhase.Complete)
        {
            if (observation == SecureDeleteRecoveryTargetObservation.ExactOriginalPresent)
            {
                return new(
                    phase,
                    observation,
                    record,
                    RequiresUserAttention: true,
                    MayRequestFreshPreMutationAuthorization: false,
                    MayAutomaticallyResumeMutation: false,
                    "The journal records primary removal, but the exact original filesystem object is still present. Recovery is contradictory and fails closed.");
            }

            if (observation == SecureDeleteRecoveryTargetObservation.DifferentObjectPresent)
            {
                return new(
                    phase,
                    observation,
                    record,
                    RequiresUserAttention: true,
                    MayRequestFreshPreMutationAuthorization: false,
                    MayAutomaticallyResumeMutation: false,
                    "The original path now belongs to a different filesystem object. The replacement is not part of the prior Secure Delete authority and must not be touched.");
            }

            return new(
                phase,
                observation,
                record,
                RequiresUserAttention: phase != SecureDeleteRecoveryPhase.Complete,
                MayRequestFreshPreMutationAuthorization: false,
                MayAutomaticallyResumeMutation: false,
                phase == SecureDeleteRecoveryPhase.Complete
                    ? "The authenticated journal is complete and the original object is not currently proven present. No mutation is resumed."
                    : "The authenticated journal records primary removal, but remaining cleanup/recovery work requires explicit review; no mutation is resumed automatically.");
        }

        return new(
            SecureDeleteRecoveryPhase.RecoveryRequired,
            observation,
            record,
            RequiresUserAttention: true,
            MayRequestFreshPreMutationAuthorization: false,
            MayAutomaticallyResumeMutation: false,
            "The operation is explicitly marked RecoveryRequired and cannot resume mutation automatically.");
    }

    private static SecureDeleteRecoveryPhase MapPhase(SecureDeleteOperationState state) => state switch
    {
        SecureDeleteOperationState.Prepared => SecureDeleteRecoveryPhase.PreMutation,
        SecureDeleteOperationState.IdentityVerified => SecureDeleteRecoveryPhase.PreMutation,
        SecureDeleteOperationState.PrimaryMutationStarted => SecureDeleteRecoveryPhase.MutationStarted,
        SecureDeleteOperationState.PrimaryRemovalVerified => SecureDeleteRecoveryPhase.RemovalRecorded,
        SecureDeleteOperationState.RelatedCleanupPending => SecureDeleteRecoveryPhase.RelatedCleanupPending,
        SecureDeleteOperationState.Complete => SecureDeleteRecoveryPhase.Complete,
        SecureDeleteOperationState.RecoveryRequired => SecureDeleteRecoveryPhase.RecoveryRequired,
        _ => SecureDeleteRecoveryPhase.JournalInvalid
    };

    private static SecureDeleteRecoveryTargetObservation ObserveTarget(SecureDeleteTargetIdentity expected)
    {
        SecureDeleteTargetValidationResult exact = SecureDeleteTargetValidator.Revalidate(expected);
        if (exact.Succeeded)
            return SecureDeleteRecoveryTargetObservation.ExactOriginalPresent;

        SecureDeleteTargetValidationResult current = SecureDeleteTargetValidator.Validate(expected.CanonicalPath);
        if (current.Succeeded)
            return SecureDeleteRecoveryTargetObservation.DifferentObjectPresent;

        if (current.Code == SecureDeleteTargetValidationCode.MissingOrInaccessible)
            return SecureDeleteRecoveryTargetObservation.MissingOrInaccessible;

        return SecureDeleteRecoveryTargetObservation.RejectedOrUnverified;
    }
}
