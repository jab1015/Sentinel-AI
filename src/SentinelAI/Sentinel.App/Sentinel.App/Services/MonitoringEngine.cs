/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    public class MonitoringEngine
    {
        private readonly SystemMonitor _systemMonitor = new();
        private readonly DiskMonitor _diskMonitor = new();
        private readonly NetworkMonitor _networkMonitor = new();
        private readonly ProcessMonitor _processMonitor = new();
        private readonly SecurityMonitor _securityMonitor = new();
        private readonly WindowsInfoMonitor _windowsInfoMonitor = new();

        public SystemSnapshot CurrentSnapshot { get; private set; } = new();

        public event EventHandler<SystemSnapshot>? SnapshotUpdated;

        public async Task RefreshAsync()
        {
            CurrentSnapshot = new SystemSnapshot
            {
                Timestamp = DateTime.Now,

                CpuUsagePercent = _systemMonitor.GetCpuUsage(),

                MemoryUsedGB = _systemMonitor.GetMemoryUsedGB(),
                MemoryTotalGB = _systemMonitor.GetMemoryTotalGB(),
                MemoryUsagePercent = _systemMonitor.GetMemoryPercent(),

                DiskUsagePercent = _diskMonitor.GetUsagePercent(),
                DiskFreeGB = _diskMonitor.GetFreeSpaceGB(),
                DiskTotalGB = _diskMonitor.GetTotalSpaceGB(),

                DownloadMbps = 0,
                UploadMbps = 0,

                ProcessCount = _processMonitor.GetProcessCount(),

                DefenderEnabled = _securityMonitor.IsWindowsDefenderInstalled(),
                FirewallEnabled = _securityMonitor.IsFirewallInstalled()
            };

            SnapshotUpdated?.Invoke(this, CurrentSnapshot);

            await Task.CompletedTask;
        }

        public string MachineName => _windowsInfoMonitor.GetMachineName();

        public string UserName => _windowsInfoMonitor.GetUserName();

        public string OperatingSystem => _windowsInfoMonitor.GetOsVersion();

        public bool Is64Bit => _windowsInfoMonitor.Is64BitOperatingSystem();

        public int ProcessorCount => _windowsInfoMonitor.ProcessorCount();

        public TimeSpan Uptime => _windowsInfoMonitor.GetSystemUptime();
    }
}