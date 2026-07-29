/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemInformation;

namespace Sentinel.App.Services
{
    public sealed class SystemMonitor
    {
        private const double BytesPerGigabyte = 1024d * 1024d * 1024d;

        private readonly object _sampleLock = new();
        private ulong _previousIdleTime;
        private ulong _previousKernelTime;
        private ulong _previousUserTime;
        private bool _hasPreviousCpuSample;

        public unsafe double GetCpuUsage()
        {
            FILETIME idleTime = default;
            FILETIME kernelTime = default;
            FILETIME userTime = default;

            if (PInvoke.GetSystemTimes(&idleTime, &kernelTime, &userTime).Value == 0)
            {
                return 0;
            }

            ulong currentIdleTime = ToUInt64(idleTime);
            ulong currentKernelTime = ToUInt64(kernelTime);
            ulong currentUserTime = ToUInt64(userTime);

            lock (_sampleLock)
            {
                if (!_hasPreviousCpuSample)
                {
                    StoreCpuSample(currentIdleTime, currentKernelTime, currentUserTime);
                    _hasPreviousCpuSample = true;
                    return 0;
                }

                if (currentIdleTime < _previousIdleTime ||
                    currentKernelTime < _previousKernelTime ||
                    currentUserTime < _previousUserTime)
                {
                    StoreCpuSample(currentIdleTime, currentKernelTime, currentUserTime);
                    return 0;
                }

                ulong idleDelta = currentIdleTime - _previousIdleTime;
                ulong kernelDelta = currentKernelTime - _previousKernelTime;
                ulong userDelta = currentUserTime - _previousUserTime;

                StoreCpuSample(currentIdleTime, currentKernelTime, currentUserTime);

                ulong totalDelta = kernelDelta + userDelta;
                if (totalDelta == 0 || idleDelta > totalDelta)
                {
                    return 0;
                }

                double cpuUsage = (totalDelta - idleDelta) * 100.0 / totalDelta;
                return Math.Clamp(cpuUsage, 0.0, 100.0);
            }
        }

        public double GetMemoryUsedGB()
        {
            MemoryUsageSnapshot snapshot = GetMemoryUsageSnapshot();
            return Math.Round(snapshot.UsedBytes / BytesPerGigabyte, 2);
        }

        public double GetMemoryTotalGB()
        {
            MemoryUsageSnapshot snapshot = GetMemoryUsageSnapshot();
            return Math.Round(snapshot.TotalBytes / BytesPerGigabyte, 2);
        }

        public double GetMemoryPercent()
        {
            MemoryUsageSnapshot snapshot = GetMemoryUsageSnapshot();
            return Math.Round(snapshot.PercentageUsed, 1);
        }

        private static unsafe MemoryUsageSnapshot GetMemoryUsageSnapshot()
        {
            MEMORYSTATUSEX memoryStatus = default;
            memoryStatus.dwLength = (uint)sizeof(MEMORYSTATUSEX);

            if (PInvoke.GlobalMemoryStatusEx(&memoryStatus).Value == 0)
            {
                return default;
            }

            ulong totalBytes = memoryStatus.ullTotalPhys;
            ulong availableBytes = Math.Min(memoryStatus.ullAvailPhys, totalBytes);
            ulong usedBytes = totalBytes - availableBytes;
            double percentageUsed = totalBytes == 0
                ? 0
                : Math.Clamp(usedBytes * 100.0 / totalBytes, 0.0, 100.0);

            return new MemoryUsageSnapshot(usedBytes, totalBytes, percentageUsed);
        }

        private void StoreCpuSample(ulong idleTime, ulong kernelTime, ulong userTime)
        {
            _previousIdleTime = idleTime;
            _previousKernelTime = kernelTime;
            _previousUserTime = userTime;
        }

        private static ulong ToUInt64(FILETIME fileTime)
        {
            return ((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;
        }

        private readonly record struct MemoryUsageSnapshot(
            ulong UsedBytes,
            ulong TotalBytes,
            double PercentageUsed);
    }
}
