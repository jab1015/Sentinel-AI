/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;

namespace Sentinel.App.Services
{
    public class DiskMonitor
    {
        public double GetTotalSpaceGB()
        {
            try
            {
                var drive = GetSystemDrive();

                return Math.Round(
                    drive.TotalSize / 1024d / 1024d / 1024d,
                    2);
            }
            catch
            {
                return 0;
            }
        }

        public double GetFreeSpaceGB()
        {
            try
            {
                var drive = GetSystemDrive();

                return Math.Round(
                    drive.TotalFreeSpace / 1024d / 1024d / 1024d,
                    2);
            }
            catch
            {
                return 0;
            }
        }

        public double GetUsedSpaceGB()
        {
            return Math.Round(
                GetTotalSpaceGB() - GetFreeSpaceGB(),
                2);
        }

        public double GetUsagePercent()
        {
            double total = GetTotalSpaceGB();

            if (total <= 0)
                return 0;

            return Math.Round(
                (GetUsedSpaceGB() / total) * 100,
                1);
        }

        private static DriveInfo GetSystemDrive()
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory)!;
            return new DriveInfo(root);
        }
    }
}