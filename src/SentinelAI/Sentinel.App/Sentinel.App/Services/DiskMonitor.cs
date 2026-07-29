/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;

namespace Sentinel.App.Services
{
    public sealed class DiskMonitor
    {
        private const double BytesPerGigabyte = 1024d * 1024d * 1024d;

        public double GetTotalSpaceGB()
        {
            DiskUsageSnapshot snapshot = GetDiskUsageSnapshot();
            return Math.Round(snapshot.TotalBytes / BytesPerGigabyte, 2);
        }

        public double GetFreeSpaceGB()
        {
            DiskUsageSnapshot snapshot = GetDiskUsageSnapshot();
            return Math.Round(snapshot.FreeBytes / BytesPerGigabyte, 2);
        }

        public double GetUsedSpaceGB()
        {
            DiskUsageSnapshot snapshot = GetDiskUsageSnapshot();
            return Math.Round(snapshot.UsedBytes / BytesPerGigabyte, 2);
        }

        public double GetUsagePercent()
        {
            DiskUsageSnapshot snapshot = GetDiskUsageSnapshot();
            return Math.Round(snapshot.UsagePercent, 1);
        }

        private static DiskUsageSnapshot GetDiskUsageSnapshot()
        {
            try
            {
                DriveInfo drive = GetSystemDrive();
                if (!drive.IsReady)
                {
                    return default;
                }

                long totalBytes = Math.Max(drive.TotalSize, 0);
                long freeBytes = Math.Clamp(drive.TotalFreeSpace, 0, totalBytes);
                long usedBytes = totalBytes - freeBytes;
                double usagePercent = totalBytes == 0
                    ? 0
                    : Math.Clamp(usedBytes * 100.0 / totalBytes, 0.0, 100.0);

                return new DiskUsageSnapshot(
                    (ulong)usedBytes,
                    (ulong)freeBytes,
                    (ulong)totalBytes,
                    usagePercent);
            }
            catch (IOException)
            {
                return default;
            }
            catch (UnauthorizedAccessException)
            {
                return default;
            }
            catch (ArgumentException)
            {
                return default;
            }
        }

        private static DriveInfo GetSystemDrive()
        {
            string? root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new InvalidOperationException("The Windows system drive could not be determined.");
            }

            return new DriveInfo(root);
        }

        private readonly record struct DiskUsageSnapshot(
            ulong UsedBytes,
            ulong FreeBytes,
            ulong TotalBytes,
            double UsagePercent);
    }
}
