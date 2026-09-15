using System;
using System.IO;

namespace Sentinel.App.Services;

/// <summary>
/// Conservative storage classification for privacy-removal planning. This foundation
/// reports only facts available from the mounted-volume abstraction and intentionally
/// leaves physical-media, BitLocker, TRIM/unmap, and cloud-sync facts Unknown until a
/// reviewed Windows/provider API supplies trustworthy evidence.
/// </summary>
internal static class StorageCapabilityDetector
{
    internal static StorageCapabilitySnapshot Detect(SecureDeleteTargetIdentity target)
    {
        if (target.IsEmpty)
            return StorageCapabilitySnapshot.Unknown(string.Empty, "No validated target identity was supplied.");

        return Detect(target.CanonicalPath);
    }

    internal static StorageCapabilitySnapshot Detect(string canonicalPath)
    {
        if (string.IsNullOrWhiteSpace(canonicalPath))
            return StorageCapabilitySnapshot.Unknown(string.Empty, "No canonical path was supplied.");

        string fullPath;
        try
        {
            if (!Path.IsPathFullyQualified(canonicalPath))
                return StorageCapabilitySnapshot.Unknown(canonicalPath, "Storage classification requires a fully qualified path.");
            fullPath = Path.GetFullPath(canonicalPath);
        }
        catch
        {
            return StorageCapabilitySnapshot.Unknown(canonicalPath, "The path could not be canonicalized for storage classification.");
        }

        string? root;
        try { root = Path.GetPathRoot(fullPath); }
        catch { root = null; }
        if (string.IsNullOrWhiteSpace(root))
            return StorageCapabilitySnapshot.Unknown(fullPath, "No volume root could be identified.");

        try
        {
            DriveInfo drive = new(root);
            StorageLocationKind location = drive.DriveType switch
            {
                DriveType.Fixed => StorageLocationKind.LocalFixed,
                DriveType.Removable => StorageLocationKind.Removable,
                DriveType.Network => StorageLocationKind.Network,
                DriveType.CDRom => StorageLocationKind.Optical,
                DriveType.Ram => StorageLocationKind.RamDisk,
                DriveType.NoRootDirectory => StorageLocationKind.NoRootDirectory,
                _ => StorageLocationKind.Unknown
            };

            string? fileSystem = null;
            if (drive.IsReady)
            {
                try { fileSystem = drive.DriveFormat; }
                catch { fileSystem = null; }
            }

            return new StorageCapabilitySnapshot(
                fullPath,
                root,
                fileSystem,
                location,
                PhysicalMediaKind.Unknown,
                CapabilityKnowledge.Unknown,
                CapabilityKnowledge.Unknown,
                CapabilityKnowledge.Unknown,
                "Volume/root/filesystem facts come from DriveInfo. Physical media, BitLocker, TRIM/unmap, and cloud-sync state remain Unknown until independently proven by reviewed platform/provider APIs.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return StorageCapabilitySnapshot.Unknown(fullPath, "The volume could not be classified without guessing.");
        }
    }
}
