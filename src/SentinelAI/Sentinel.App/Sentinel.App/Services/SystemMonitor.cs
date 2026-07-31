/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Threading;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Sentinel.App.Services
{
    public sealed class SystemMonitor
    {
        private const double BytesPerGigabyte = 1024d * 1024d * 1024d;
        private const int InitialCpuSampleDelayMilliseconds = 150;

        private readonly object _sampleLock = new();
        private ulong _previousIdleTime;
        private ulong _previousKernelTime;
        private ulong _previousUserTime;
        private bool _hasPreviousCpuSample;

        public unsafe double GetCpuUsage()
        {
            if (!TryReadCpuTimes(out ulong currentIdleTime, out ulong currentKernelTime, out ulong currentUserTime))
            {
                return 0;
            }

            lock (_sampleLock)
            {
                if (!_hasPreviousCpuSample)
                {
                    StoreCpuSample(currentIdleTime, currentKernelTime, currentUserTime);
                    _hasPreviousCpuSample = true;

                    // GetSystemTimes requires two samples to calculate utilization.
                    // Take a short warm-up sample now so the first dashboard refresh
                    // can show a real CPU value instead of waiting for another cycle.
                    Thread.Sleep(InitialCpuSampleDelayMilliseconds);

                    if (!TryReadCpuTimes(out currentIdleTime, out currentKernelTime, out currentUserTime))
                    {
                        return 0;
                    }
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

        private static unsafe bool TryReadCpuTimes(out ulong idle, out ulong kernel, out ulong user)
        {
            FILETIME idleTime = default;
            FILETIME kernelTime = default;
            FILETIME userTime = default;

            if (PInvoke.GetSystemTimes(&idleTime, &kernelTime, &userTime).Value == 0)
            {
                idle = kernel = user = 0;
                return false;
            }

            idle = ToUInt64(idleTime);
            kernel = ToUInt64(kernelTime);
            user = ToUInt64(userTime);
            return true;
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
            return ((ulong)(uint)fileTime.dwHighDateTime << 32) |
                   (uint)fileTime.dwLowDateTime;
        }

        private readonly record struct MemoryUsageSnapshot(
            ulong UsedBytes,
            ulong TotalBytes,
            double PercentageUsed);
    }
}
