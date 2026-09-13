using System;
using System.Runtime.InteropServices;

namespace Sentinel.App.Services;

internal enum SecureDeletePrimaryRemovalStatus
{
    Verified = 0,
    Failed,
    Unknown
}

internal enum SecureDeleteMediaActionStatus
{
    Verified = 0,
    Requested,
    NotSupported,
    CannotProve
}

internal enum SecureDeletePhysicalMediaAbsenceStatus
{
    CannotProve = 0,
    Verified
}

internal sealed record SecureDeleteExecutionResult(
    bool Succeeded,
    string Code,
    string Message,
    Guid OperationId,
    SecureDeleteOperationState? JournalState,
    SecureDeletePrimaryRemovalStatus PrimaryRemoval,
    SecureDeleteMediaActionStatus MediaAction,
    SecureDeletePhysicalMediaAbsenceStatus PhysicalMediaAbsence,
    bool ReplacementObjectPresent,
    bool RelatedCleanupRequired)
{
    internal static SecureDeleteExecutionResult Fail(
        string code,
        string message,
        Guid operationId = default,
        SecureDeleteOperationState? journalState = null,
        SecureDeletePrimaryRemovalStatus primaryRemoval = SecureDeletePrimaryRemovalStatus.Failed) =>
        new(false, code, message, operationId, journalState, primaryRemoval,
            SecureDeleteMediaActionStatus.NotSupported,
            SecureDeletePhysicalMediaAbsenceStatus.CannotProve,
            ReplacementObjectPresent: false,
            RelatedCleanupRequired: false);
}

/// <summary>
/// Executes only the narrow logical removal of a previously-authorized exact filesystem
/// object. It accepts no pathname and exposes no generic delete primitive. Mutation is
/// performed only through the retained, independently verified SecureDeleteMutationLease.
/// </summary>
internal sealed class SecureDeleteExactObjectExecutor
{
    private const uint InvalidFileAttributes = 0xFFFFFFFF;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    private readonly SecureDeleteCoordinator _coordinator;
    private readonly SecureDeleteMutationLeaseManager _leaseManager;
    private readonly SecureDeleteOperationJournal _journal;

    internal SecureDeleteExactObjectExecutor(
        SecureDeleteCoordinator coordinator,
        SecureDeleteOperationJournal journal)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _leaseManager = new SecureDeleteMutationLeaseManager(_coordinator);
    }

    internal SecureDeleteExecutionResult Execute(SecureDeleteAuthorization? authorization)
    {
        SecureDeleteMutationGateResult gate = _coordinator.RevalidateForMutation(authorization);
        if (!gate.Succeeded || authorization is null)
            return SecureDeleteExecutionResult.Fail("AuthorizationRejected", gate.Message);

        SecureDeleteMutationLeaseResult leaseResult = _leaseManager.Acquire(authorization);
        if (!leaseResult.Succeeded || leaseResult.Lease is null)
            return SecureDeleteExecutionResult.Fail("LeaseRejected", leaseResult.Message);

        SecureDeleteMutationLease lease = leaseResult.Lease;
        SecureDeleteOperationRecord? record = null;
        bool mutationStarted = false;
        try
        {
            if (lease.AuthorizationId != authorization.AuthorizationId ||
                lease.Target != gate.Target ||
                !string.Equals(lease.Storage.VolumeRoot, authorization.VolumeRoot, StringComparison.OrdinalIgnoreCase) ||
                lease.Storage.LocationKind != authorization.LocationKind ||
                !string.Equals(lease.Storage.FileSystem, authorization.FileSystem, StringComparison.OrdinalIgnoreCase))
            {
                return SecureDeleteExecutionResult.Fail("LeaseBoundaryMismatch",
                    "The retained object lease no longer matches the authorized target/storage boundary.");
            }

            record = _journal.Begin(authorization);
            record = _journal.Advance(record, SecureDeleteOperationState.IdentityVerified,
                "Retained exact-object identity and storage boundary verified.");
            record = _journal.ReadRequired(record.OperationId);

            record = _journal.Advance(record, SecureDeleteOperationState.PrimaryMutationStarted,
                "Exact-handle logical removal is about to be requested; no path delete is authorized.");
            record = _journal.ReadRequired(record.OperationId);
            mutationStarted = true;

            if (!lease.TryRequestLogicalRemoval(out int win32Error))
            {
                record = TryMarkRecoveryRequired(record,
                    "Exact-handle logical removal request failed with Win32 error " + win32Error + ".");
                return SecureDeleteExecutionResult.Fail(
                    "LogicalRemovalRequestFailed",
                    "Windows rejected the exact-handle logical removal request.",
                    record.OperationId,
                    record.State,
                    SecureDeletePrimaryRemovalStatus.Failed);
            }

            // Deletion is completed by Windows when the retained exact-object handle closes.
            lease.Dispose();

            PostRemovalObservation observation = ObserveAfterRemoval(authorization.Target);
            if (observation is PostRemovalObservation.OriginalStillPresent or PostRemovalObservation.Unknown)
            {
                record = TryMarkRecoveryRequired(record,
                    observation == PostRemovalObservation.OriginalStillPresent
                        ? "The exact original object was still present after the delete-pending handle closed."
                        : "Post-removal filesystem evidence was ambiguous; success cannot be proven.");
                return SecureDeleteExecutionResult.Fail(
                    observation == PostRemovalObservation.OriginalStillPresent ? "OriginalStillPresent" : "RemovalUnproven",
                    observation == PostRemovalObservation.OriginalStillPresent
                        ? "The exact original object remains present; Secure Delete failed closed."
                        : "Sentinel cannot prove that the exact original object is absent.",
                    record.OperationId,
                    record.State,
                    observation == PostRemovalObservation.OriginalStillPresent
                        ? SecureDeletePrimaryRemovalStatus.Failed
                        : SecureDeletePrimaryRemovalStatus.Unknown);
            }

            bool replacementPresent = observation == PostRemovalObservation.DifferentObjectPresent;
            record = _journal.Advance(record, SecureDeleteOperationState.PrimaryRemovalVerified,
                replacementPresent
                    ? "The original stable identity is absent; the path now belongs to a different object that is outside this authorization."
                    : "The original stable identity is absent and the original path is not present.");
            record = _journal.ReadRequired(record.OperationId);
            record = _journal.Advance(record, SecureDeleteOperationState.RelatedCleanupPending,
                "Primary logical removal verified. Related-copy/history discovery remains required before completion.");
            record = _journal.ReadRequired(record.OperationId);

            return new SecureDeleteExecutionResult(
                true,
                "PrimaryRemovalVerified",
                replacementPresent
                    ? "The authorized exact object was removed. A replacement object exists at the prior path and was not touched."
                    : "The authorized exact object was logically removed and its absence was verified.",
                record.OperationId,
                record.State,
                SecureDeletePrimaryRemovalStatus.Verified,
                SecureDeleteMediaActionStatus.NotSupported,
                SecureDeletePhysicalMediaAbsenceStatus.CannotProve,
                replacementPresent,
                RelatedCleanupRequired: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.IO.IOException)
        {
            if (record is not null && mutationStarted)
                record = TryMarkRecoveryRequired(record, "Secure Delete executor stopped after mutation-start persistence: " + ex.GetType().Name + ".");

            return SecureDeleteExecutionResult.Fail(
                "ExecutionFailure",
                "Secure Delete stopped safely; the operation requires review before any retry.",
                record?.OperationId ?? Guid.Empty,
                record?.State,
                mutationStarted ? SecureDeletePrimaryRemovalStatus.Unknown : SecureDeletePrimaryRemovalStatus.Failed);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private SecureDeleteOperationRecord TryMarkRecoveryRequired(SecureDeleteOperationRecord record, string detail)
    {
        try
        {
            if (record.State is SecureDeleteOperationState.Complete or SecureDeleteOperationState.RecoveryRequired)
                return record;
            return _journal.Advance(record, SecureDeleteOperationState.RecoveryRequired, detail);
        }
        catch
        {
            // Never replace the original failure with a journal-cleanup exception. The caller
            // already receives UNKNOWN/FAILED and cannot treat the operation as successful.
            return record;
        }
    }

    private static PostRemovalObservation ObserveAfterRemoval(SecureDeleteTargetIdentity expected)
    {
        SecureDeleteTargetValidationResult exact = SecureDeleteTargetValidator.Revalidate(expected);
        if (exact.Succeeded)
            return PostRemovalObservation.OriginalStillPresent;

        SecureDeleteTargetValidationResult current = SecureDeleteTargetValidator.Validate(expected.CanonicalPath);
        if (current.Succeeded)
        {
            bool sameIdentity = current.Target.VolumeSerialNumber == expected.VolumeSerialNumber &&
                                current.Target.FileIdLow == expected.FileIdLow &&
                                current.Target.FileIdHigh == expected.FileIdHigh;
            return sameIdentity
                ? PostRemovalObservation.OriginalStillPresent
                : PostRemovalObservation.DifferentObjectPresent;
        }

        uint attributes = GetFileAttributesW(expected.CanonicalPath);
        if (attributes == InvalidFileAttributes)
        {
            int error = Marshal.GetLastWin32Error();
            if (error is ErrorFileNotFound or ErrorPathNotFound)
                return PostRemovalObservation.Missing;
        }

        return PostRemovalObservation.Unknown;
    }

    private enum PostRemovalObservation
    {
        Missing = 0,
        DifferentObjectPresent,
        OriginalStillPresent,
        Unknown
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileAttributesW(string fileName);
}
