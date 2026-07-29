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
        private readonly EventLogMonitor _eventLogMonitor = new();
        private readonly WindowsInfoMonitor _windowsInfoMonitor = new();

        public SystemSnapshot CurrentSnapshot { get; private set; } = new();

        public event EventHandler<SystemSnapshot>? SnapshotUpdated;

        public async Task RefreshAsync()
        {
            NetworkMonitor.NetworkThroughputSnapshot networkSnapshot =
                _networkMonitor.GetThroughput();

            SecurityMonitor.SecurityStatusSnapshot securitySnapshot =
                _securityMonitor.GetStatus();

            EventLogMonitor.EventLogStatusSnapshot eventLogSnapshot =
                _eventLogMonitor.GetStatus();

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

                DownloadMbps = networkSnapshot.DownloadMbps,
                UploadMbps = networkSnapshot.UploadMbps,

                ProcessCount = _processMonitor.GetProcessCount(),
                HighestMemoryProcessName = _processMonitor.GetHighestMemoryProcess(),
                HighestMemoryProcessGB = _processMonitor.GetHighestMemoryProcessGB(),

                DefenderEnabled = securitySnapshot.DefenderStatus == "Enabled",
                FirewallEnabled = securitySnapshot.FirewallStatus == "Enabled",
                DefenderStatus = securitySnapshot.DefenderStatus,
                FirewallStatus = securitySnapshot.FirewallStatus,

                CriticalEventCount = eventLogSnapshot.CriticalCount,
                ErrorEventCount = eventLogSnapshot.ErrorCount,
                LatestEventTime = eventLogSnapshot.LatestEventTime,
                LatestEventSource = eventLogSnapshot.LatestEventSource,
                LatestEventMessage = eventLogSnapshot.LatestEventMessage
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
