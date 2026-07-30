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
        private static readonly TimeSpan ProcessRefreshInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ServiceRefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SecurityRefreshInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan EventLogRefreshInterval = TimeSpan.FromSeconds(30);

        private readonly SystemMonitor _systemMonitor = new();
        private readonly DiskMonitor _diskMonitor = new();
        private readonly NetworkMonitor _networkMonitor = new();
        private readonly ProcessMonitor _processMonitor = new();
        private readonly ServiceMonitor _serviceMonitor = new();
        private readonly SecurityMonitor _securityMonitor = new();
        private readonly EventLogMonitor _eventLogMonitor = new();
        private readonly RiskAssessmentEngine _riskAssessmentEngine = new();
        private readonly GuidanceEngine _guidanceEngine = new();
        private readonly WindowsInfoMonitor _windowsInfoMonitor = new();

        private ProcessMonitor.ProcessIntelligenceSnapshot _processSnapshot =
            new(0, "Loading...", 0, 0, "None", "Process analysis is loading.");
        private ServiceMonitor.ServiceIntelligenceSnapshot _serviceSnapshot =
            new(0, 0, 0, "None", "Service analysis is loading.");
        private SecurityMonitor.SecurityStatusSnapshot _securitySnapshot =
            new("Loading...", "Loading...");
        private EventLogMonitor.EventLogStatusSnapshot _eventLogSnapshot =
            new(0, 0, null, "None", "Event Log analysis is loading.");

        private DateTime _lastProcessRefresh = DateTime.MinValue;
        private DateTime _lastServiceRefresh = DateTime.MinValue;
        private DateTime _lastSecurityRefresh = DateTime.MinValue;
        private DateTime _lastEventLogRefresh = DateTime.MinValue;

        public SystemSnapshot CurrentSnapshot { get; private set; } = new();
        public event EventHandler<SystemSnapshot>? SnapshotUpdated;

        public async Task RefreshAsync()
        {
            DateTime now = DateTime.Now;

            Task processTask = RefreshProcessDataIfDueAsync(now);
            Task serviceTask = RefreshServiceDataIfDueAsync(now);
            Task securityTask = RefreshSecurityDataIfDueAsync(now);
            Task eventLogTask = RefreshEventLogDataIfDueAsync(now);

            await Task.WhenAll(processTask, serviceTask, securityTask, eventLogTask);

            NetworkMonitor.NetworkThroughputSnapshot networkSnapshot =
                _networkMonitor.GetThroughput();

            SystemSnapshot snapshot = new()
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
                ProcessCount = _processSnapshot.TotalProcessCount,
                HighestMemoryProcessName = _processSnapshot.HighestMemoryProcessName,
                HighestMemoryProcessGB = _processSnapshot.HighestMemoryProcessGB,
                FlaggedProcessCount = _processSnapshot.FlaggedProcessCount,
                PrimaryFlaggedProcessName = _processSnapshot.PrimaryProcessName,
                PrimaryFlaggedProcessReason = _processSnapshot.PrimaryReason,
                InstalledServiceCount = _serviceSnapshot.InstalledServiceCount,
                RunningServiceCount = _serviceSnapshot.RunningServiceCount,
                FlaggedServiceCount = _serviceSnapshot.FlaggedServiceCount,
                PrimaryFlaggedServiceName = _serviceSnapshot.PrimaryServiceName,
                PrimaryFlaggedServiceReason = _serviceSnapshot.PrimaryReason,
                DefenderEnabled = _securitySnapshot.DefenderStatus == "Enabled",
                FirewallEnabled = _securitySnapshot.FirewallStatus == "Enabled",
                DefenderStatus = _securitySnapshot.DefenderStatus,
                FirewallStatus = _securitySnapshot.FirewallStatus,
                CriticalEventCount = _eventLogSnapshot.CriticalCount,
                ErrorEventCount = _eventLogSnapshot.ErrorCount,
                LatestEventTime = _eventLogSnapshot.LatestEventTime,
                LatestEventSource = _eventLogSnapshot.LatestEventSource,
                LatestEventMessage = _eventLogSnapshot.LatestEventMessage
            };

            RiskAssessmentEngine.RiskAssessment assessment =
                _riskAssessmentEngine.Assess(snapshot);

            snapshot.RiskScore = assessment.Score;
            snapshot.RiskLevel = assessment.Level;
            snapshot.RiskSummary = assessment.Summary;
            snapshot.Recommendation = assessment.Recommendation;

            GuidanceEngine.GuidanceResult guidance = _guidanceEngine.Analyze(snapshot);
            snapshot.GuidanceTitle = guidance.Title;
            snapshot.GuidanceSeverity = guidance.Severity;
            snapshot.GuidanceWhatHappened = guidance.WhatHappened;
            snapshot.GuidanceWhyItMatters = guidance.WhyItMatters;
            snapshot.GuidanceRecommendedAction = guidance.RecommendedAction;
            snapshot.GuidanceFixAvailability = guidance.FixAvailability;
            snapshot.GuidanceFixDetails = guidance.FixDetails;
            snapshot.GuidanceActionId = guidance.ActionId;
            snapshot.GuidanceActionLabel = guidance.ActionLabel;

            CurrentSnapshot = snapshot;
            SnapshotUpdated?.Invoke(this, CurrentSnapshot);
        }

        private async Task RefreshProcessDataIfDueAsync(DateTime now)
        {
            if (now - _lastProcessRefresh < ProcessRefreshInterval)
            {
                return;
            }

            _lastProcessRefresh = now;
            _processSnapshot = await Task.Run(_processMonitor.GetIntelligence);
        }

        private async Task RefreshServiceDataIfDueAsync(DateTime now)
        {
            if (now - _lastServiceRefresh < ServiceRefreshInterval)
            {
                return;
            }

            _lastServiceRefresh = now;
            _serviceSnapshot = await Task.Run(_serviceMonitor.GetIntelligence);
        }

        private async Task RefreshSecurityDataIfDueAsync(DateTime now)
        {
            if (now - _lastSecurityRefresh < SecurityRefreshInterval)
            {
                return;
            }

            _lastSecurityRefresh = now;
            _securitySnapshot = await Task.Run(_securityMonitor.GetStatus);
        }

        private async Task RefreshEventLogDataIfDueAsync(DateTime now)
        {
            if (now - _lastEventLogRefresh < EventLogRefreshInterval)
            {
                return;
            }

            _lastEventLogRefresh = now;
            _eventLogSnapshot = await Task.Run(_eventLogMonitor.GetStatus);
        }

        public string MachineName => _windowsInfoMonitor.GetMachineName();
        public string UserName => _windowsInfoMonitor.GetUserName();
        public string OperatingSystem => _windowsInfoMonitor.GetOsVersion();
        public bool Is64Bit => _windowsInfoMonitor.Is64BitOperatingSystem();
        public int ProcessorCount => _windowsInfoMonitor.ProcessorCount();
        public TimeSpan Uptime => _windowsInfoMonitor.GetSystemUptime();
    }
}
