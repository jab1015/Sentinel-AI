using System;

namespace Sentinel.App.Services;

internal enum SecureDeleteTargetValidationCode
{
    Valid = 0,
    InvalidPath,
    UnsupportedNamespace,
    MissingOrInaccessible,
    DirectoryRejected,
    ReparsePointRejected,
    MultipleLinksRejected,
    ProtectedLocation,
    SystemCriticalFile,
    FinalPathUnverified,
    IdentityUnverified,
    IdentityChanged
}

internal readonly record struct SecureDeleteTargetIdentity(
    ulong VolumeSerialNumber,
    ulong FileIdLow,
    ulong FileIdHigh,
    string CanonicalPath,
    long FileLength)
{
    internal bool IsEmpty => string.IsNullOrWhiteSpace(CanonicalPath);
}

internal sealed record SecureDeleteTargetValidationResult(
    bool Succeeded,
    SecureDeleteTargetValidationCode Code,
    string Message,
    SecureDeleteTargetIdentity Target)
{
    internal static SecureDeleteTargetValidationResult Success(SecureDeleteTargetIdentity target) =>
        new(true, SecureDeleteTargetValidationCode.Valid, "Exact filesystem target validated.", target);

    internal static SecureDeleteTargetValidationResult Failure(
        SecureDeleteTargetValidationCode code,
        string message) =>
        new(false, code, message, default);
}

internal enum StorageLocationKind
{
    Unknown = 0,
    LocalFixed,
    Removable,
    Network,
    Optical,
    RamDisk,
    NoRootDirectory
}

internal enum PhysicalMediaKind
{
    Unknown = 0,
    RotationalHdd,
    SataSsd,
    Nvme,
    OtherFlash
}

internal enum CapabilityKnowledge
{
    Unknown = 0,
    No,
    Yes
}

internal sealed record StorageCapabilitySnapshot(
    string CanonicalPath,
    string? VolumeRoot,
    string? FileSystem,
    StorageLocationKind LocationKind,
    PhysicalMediaKind PhysicalMedia,
    CapabilityKnowledge BitLockerProtected,
    CapabilityKnowledge TrimOrUnmapSupported,
    CapabilityKnowledge CloudSynchronized,
    string EvidenceNote)
{
    internal static StorageCapabilitySnapshot Unknown(string canonicalPath, string note) =>
        new(canonicalPath, null, null, StorageLocationKind.Unknown, PhysicalMediaKind.Unknown,
            CapabilityKnowledge.Unknown, CapabilityKnowledge.Unknown, CapabilityKnowledge.Unknown, note);
}
