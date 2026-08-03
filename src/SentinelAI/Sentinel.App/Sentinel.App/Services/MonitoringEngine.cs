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
        private static readonly TimeSpan MemoryInvestigationRefreshInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ProcessLineageRefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan CommandLineRefreshInterval = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ServiceRefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SecurityRefreshInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan EventLogRefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan StartupRefreshInterval = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ScheduledTaskRefreshInterval = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ActiveConnectionRefreshInterval = TimeSpan.FromSeconds(5);

        private readonly SystemMonitor _systemMonitor = new();
        private readonly DiskMonitor _diskMonitor = new();
        private readonly NetworkMonitor _networkMonitor = new();
        private readonly ProcessMonitor _processMonitor = new();
        private readonly MemoryInvestigationMonitor _memoryInvestigationMonitor = new();
        private readonly ProcessLineageMonitor _processLineageMonitor = new();
        private readonly CommandLineMonitor _commandLineMonitor = new();
        private readonly ServiceMonitor _serviceMonitor = new();
        private readonly SecurityMonitor _securityMonitor = new();
        private readonly EventLogMonitor _eventLogMonitor = new();
        private readonly StartupPersistenceMonitor _startupPersistenceMonitor = new();
        private readonly ScheduledTaskMonitor _scheduledTaskMonitor = new();
        private readonly ActiveConnectionMonitor _activeConnectionMonitor = new();
        private readonly RiskAssessmentEngine _riskAssessmentEngine = new();
        private readonly GuidanceEngine _guidanceEngine = new();
        private readonly InvestigationEngine _investigationEngine = new();
        private readonly RemediationRecommendationEngine _remediationRecommendationEngine = new();
        private readonly AutonomousProtectionCoordinator _autonomousProtectionCoordinator = new();
        private readonly AutonomousProtectionExecutor _autonomousProtectionExecutor = new();
        private readonly InvestigationRecurrenceTracker _recurrenceTracker = new();
        private readonly WindowsInfoMonitor _windowsInfoMonitor = new();

        private ProcessMonitor.ProcessIntelligenceSnapshot _processSnapshot = new(0, "Loading...", 0, 0, "None", "Process analysis is loading.");
        private MemoryInvestigationMonitor.MemoryInvestigationSnapshot _memoryInvestigationSnapshot = new(MemoryInvestigationMonitor.MemoryPressureLevel.Normal, 0, 0, "Memory investigation is loading.", "Memory investigation is loading.", "No action is required while Sentinel collects memory context.");
        private ProcessLineageMonitor.ProcessLineageSnapshot _processLineageSnapshot = new(0, 0, "None", "None", "Process lineage analysis is loading.");
        private CommandLineMonitor.CommandLineSnapshot _commandLineSnapshot = new(0, 0, "None", "Command-line analysis is loading.", "None");
        private ServiceMonitor.ServiceIntelligenceSnapshot _serviceSnapshot = new(0, 0, 0, "None", "Service analysis is loading.");
        private SecurityMonitor.SecurityStatusSnapshot _securitySnapshot = new("Loading...", "Loading...");
        private EventLogMonitor.EventLogStatusSnapshot _eventLogSnapshot = new(0, 0, null, "None", "Event Log analysis is loading.");
        private StartupPersistenceMonitor.StartupPersistenceSnapshot _startupSnapshot = new(0, 0, "None", "Startup persistence analysis is loading.");
        private ScheduledTaskMonitor.ScheduledTaskSnapshot _scheduledTaskSnapshot = new(0, 0, "None", "Scheduled-task analysis is loading.");
        private ActiveConnectionMonitor.ActiveConnectionSnapshot _activeConnectionSnapshot = new(0, 0, 0, "None", "None", "Active connection analysis is loading.");

        private DateTime _lastProcessRefresh = DateTime.MinValue;
        private DateTime _lastMemoryInvestigationRefresh = DateTime.MinValue;
        private DateTime _lastProcessLineageRefresh = DateTime.MinValue;
        private DateTime _lastCommandLineRefresh = DateTime.MinValue;
        private DateTime _lastServiceRefresh = DateTime.MinValue;
        private DateTime _lastSecurityRefresh = DateTime.MinValue;
        private DateTime _lastEventLogRefresh = DateTime.MinValue;
        private DateTime _lastStartupRefresh = DateTime.MinValue;
        private DateTime _lastScheduledTaskRefresh = DateTime.MinValue;
        private DateTime _lastActiveConnectionRefresh = DateTime.MinValue;

        public SystemSnapshot CurrentSnapshot { get; private set; } = new();
        public event EventHandler<SystemSnapshot>? SnapshotUpdated;

        public async Task RefreshAsync()
        {
            DateTime now = DateTime.Now;
            double memoryUsagePercent = _systemMonitor.GetMemoryPercent();
            await Task.WhenAll(RefreshProcessDataIfDueAsync(now), RefreshMemoryInvestigationDataIfDueAsync(now, memoryUsagePercent), RefreshProcessLineageDataIfDueAsync(now), RefreshCommandLineDataIfDueAsync(now), RefreshServiceDataIfDueAsync(now), RefreshSecurityDataIfDueAsync(now), RefreshEventLogDataIfDueAsync(now), RefreshStartupDataIfDueAsync(now), RefreshScheduledTaskDataIfDueAsync(now), RefreshActiveConnectionDataIfDueAsync(now));

            NetworkMonitor.NetworkThroughputSnapshot networkSnapshot = _networkMonitor.GetThroughput();
            SystemSnapshot snapshot = new()
            {
                Timestamp = DateTime.Now,
                CpuUsagePercent = _systemMonitor.GetCpuUsage(), MemoryUsedGB = _systemMonitor.GetMemoryUsedGB(), MemoryTotalGB = _systemMonitor.GetMemoryTotalGB(), MemoryUsagePercent = memoryUsagePercent,
                MemoryPressureLevel = _memoryInvestigationSnapshot.PressureLevel.ToString(), MemoryCompressionGB = _memoryInvestigationSnapshot.MemoryCompressionGB, MemoryTopContributors = _memoryInvestigationSnapshot.TopContributors, MemoryConclusion = _memoryInvestigationSnapshot.Conclusion, MemoryRecommendation = _memoryInvestigationSnapshot.Recommendation,
                DiskUsagePercent = _diskMonitor.GetUsagePercent(), DiskFreeGB = _diskMonitor.GetFreeSpaceGB(), DiskTotalGB = _diskMonitor.GetTotalSpaceGB(), DownloadMbps = networkSnapshot.DownloadMbps, UploadMbps = networkSnapshot.UploadMbps,
                ProcessCount = _processSnapshot.TotalProcessCount, HighestMemoryProcessName = _processSnapshot.HighestMemoryProcessName, HighestMemoryProcessGB = _processSnapshot.HighestMemoryProcessGB, FlaggedProcessCount = _processSnapshot.FlaggedProcessCount, PrimaryFlaggedProcessName = _processSnapshot.PrimaryProcessName, PrimaryFlaggedProcessReason = _processSnapshot.PrimaryReason,
                ProcessRelationshipCount = _processLineageSnapshot.RelationshipCount, FlaggedProcessRelationshipCount = _processLineageSnapshot.ReviewRelationshipCount, PrimaryLineageChildProcessName = _processLineageSnapshot.PrimaryChildProcessName, PrimaryLineageParentProcessName = _processLineageSnapshot.PrimaryParentProcessName, PrimaryLineageReason = _processLineageSnapshot.PrimaryReason,
                ReviewedCommandLineProcessCount = _commandLineSnapshot.ReviewedProcessCount, FlaggedCommandLineCount = _commandLineSnapshot.ReviewFindingCount, PrimaryCommandLineProcessName = _commandLineSnapshot.PrimaryProcessName, PrimaryCommandLineReason = _commandLineSnapshot.PrimaryReason, PrimaryCommandLineSummary = _commandLineSnapshot.PrimaryCommandLineSummary,
                InstalledServiceCount = _serviceSnapshot.InstalledServiceCount, RunningServiceCount = _serviceSnapshot.RunningServiceCount, FlaggedServiceCount = _serviceSnapshot.FlaggedServiceCount, PrimaryFlaggedServiceName = _serviceSnapshot.PrimaryServiceName, PrimaryFlaggedServiceReason = _serviceSnapshot.PrimaryReason,
                StartupEntryCount = _startupSnapshot.TotalEntryCount, FlaggedStartupEntryCount = _startupSnapshot.ReviewEntryCount, PrimaryFlaggedStartupEntryName = _startupSnapshot.PrimaryEntryName, PrimaryFlaggedStartupEntryReason = _startupSnapshot.PrimaryReason,
                ScheduledTaskCount = _scheduledTaskSnapshot.TotalTaskCount, FlaggedScheduledTaskCount = _scheduledTaskSnapshot.ReviewTaskCount, PrimaryFlaggedScheduledTaskName = _scheduledTaskSnapshot.PrimaryTaskName, PrimaryFlaggedScheduledTaskReason = _scheduledTaskSnapshot.PrimaryReason,
                EstablishedConnectionCount = _activeConnectionSnapshot.EstablishedConnectionCount, ExternalConnectionCount = _activeConnectionSnapshot.ExternalConnectionCount, FlaggedConnectionCount = _activeConnectionSnapshot.ReviewConnectionCount, PrimaryFlaggedConnectionProcessName = _activeConnectionSnapshot.PrimaryProcessName, PrimaryFlaggedConnectionRemoteEndpoint = _activeConnectionSnapshot.PrimaryRemoteEndpoint, PrimaryFlaggedConnectionReason = _activeConnectionSnapshot.PrimaryReason,
                ListeningTcpEndpointCount = _activeConnectionSnapshot.ListeningTcpEndpointCount, UdpEndpointCount = _activeConnectionSnapshot.UdpEndpointCount, AttributedExternalConnectionCount = _activeConnectionSnapshot.AttributedExternalConnectionCount, NetworkConnectionMonitoringAvailable = _activeConnectionSnapshot.CollectionAvailable, NetworkConnectionMonitoringStatus = _activeConnectionSnapshot.CollectionAvailable ? "Active" : "Unavailable",
                DefenderEnabled = _securitySnapshot.DefenderStatus == "Enabled", FirewallEnabled = _securitySnapshot.FirewallStatus == "Enabled", DefenderStatus = _securitySnapshot.DefenderStatus, FirewallStatus = _securitySnapshot.FirewallStatus,
                CriticalEventCount = _eventLogSnapshot.CriticalCount, ErrorEventCount = _eventLogSnapshot.ErrorCount, LatestEventTime = _eventLogSnapshot.LatestEventTime, LatestEventSource = _eventLogSnapshot.LatestEventSource, LatestEventMessage = _eventLogSnapshot.LatestEventMessage
            };

            SuppressNonActionableStorageSpacesSmp(snapshot); SuppressTransientWindowsUpdateFileInUse(snapshot);
            RiskAssessmentEngine.RiskAssessment assessment = _riskAssessmentEngine.Assess(snapshot); snapshot.RiskScore = assessment.Score; snapshot.RiskLevel = assessment.Level; snapshot.RiskSummary = assessment.Summary; snapshot.Recommendation = assessment.Recommendation;
            GuidanceEngine.GuidanceResult guidance = _guidanceEngine.Analyze(snapshot); snapshot.GuidanceTitle = guidance.Title; snapshot.GuidanceSeverity = guidance.Severity; snapshot.GuidanceConfidencePercent = guidance.ConfidencePercent; snapshot.GuidanceConfidenceLabel = guidance.ConfidenceLabel; snapshot.GuidanceEvidence = guidance.Evidence; snapshot.GuidanceWhatHappened = guidance.WhatHappened; snapshot.GuidanceWhyItMatters = guidance.WhyItMatters; snapshot.GuidanceRecommendedAction = guidance.RecommendedAction; snapshot.GuidanceFixAvailability = guidance.FixAvailability; snapshot.GuidanceFixDetails = guidance.FixDetails; snapshot.GuidanceActionId = guidance.ActionId; snapshot.GuidanceActionLabel = guidance.ActionLabel;
            InvestigationEngine.InvestigationResult investigation = _investigationEngine.Investigate(snapshot); snapshot.InvestigationState = investigation.State.ToString(); snapshot.InvestigationConclusion = investigation.Conclusion; snapshot.InvestigationSummary = investigation.Summary; snapshot.InvestigationRequiresAttention = investigation.RequiresAttention; snapshot.InvestigationReasonCode = investigation.ReasonCode;
            InvestigationRecurrenceTracker.RecurrenceResult recurrence = _recurrenceTracker.Record(investigation.ReasonCode, investigation.RequiresAttention, DateTimeOffset.Now); snapshot.InvestigationRecurrenceCount = recurrence.Count; snapshot.InvestigationIsRecurring = recurrence.IsRecurring; snapshot.InvestigationShouldEscalate = recurrence.ShouldEscalate;
            RemediationRecommendationEngine.RemediationRecommendation remediation = _remediationRecommendationEngine.Evaluate(snapshot); snapshot.RemediationAvailable = remediation.Available; snapshot.RemediationRequiresUserApproval = remediation.RequiresUserApproval; snapshot.RemediationAction = remediation.Action; snapshot.RemediationTarget = remediation.Target; snapshot.RemediationSummary = remediation.Summary;
            AutonomousProtectionCoordinator.AutonomousProtectionDecision protection = _autonomousProtectionCoordinator.Evaluate(snapshot); snapshot.AutonomousProtectionCanExecute = protection.CanExecuteAutomatically; snapshot.AutonomousProtectionRequiresUserApproval = protection.RequiresUserApproval; snapshot.AutonomousProtectionAction = protection.Action; snapshot.AutonomousProtectionTarget = protection.Target; snapshot.AutonomousProtectionSummary = protection.Summary;

            if (protection.CanExecuteAutomatically && !protection.RequiresUserApproval)
            {
                AutonomousProtectionExecutor.AutonomousProtectionExecutionResult execution = await _autonomousProtectionExecutor.ExecuteAsync(snapshot, protection, RefreshSecurityStateForProtectionAsync, RetryTransientOperationForProtectionAsync);
                snapshot.AutonomousProtectionAttempted = execution.Attempted; snapshot.AutonomousProtectionSucceeded = execution.Succeeded; snapshot.AutonomousProtectionCompletedAt = execution.CompletedAt; snapshot.AutonomousProtectionOutcomeTitle = execution.Title; snapshot.AutonomousProtectionOutcomeSummary = execution.Summary;
            }

            CurrentSnapshot = snapshot; SnapshotUpdated?.Invoke(this, CurrentSnapshot);
        }

        private async Task RefreshSecurityStateForProtectionAsync() { _securitySnapshot = await Task.Run(_securityMonitor.GetStatus); _lastSecurityRefresh = DateTime.Now; }
        private async Task RetryTransientOperationForProtectionAsync() { _eventLogSnapshot = await Task.Run(_eventLogMonitor.GetStatus); _lastEventLogRefresh = DateTime.Now; }

        public async Task<VerificationResult> VerifyCurrentGuidanceAsync()
        {
            SystemSnapshot snapshot = CurrentSnapshot;
            if (!Contains(snapshot.LatestEventSource, "Service Control Manager") || !Contains(snapshot.LatestEventMessage, "terminated unexpectedly")) { await RefreshAsync(); return new VerificationResult("Check complete", "Sentinel AI refreshed the current monitoring data. No targeted service verification was available for this finding.", false); }
            string serviceName = ExtractServiceDisplayName(snapshot.LatestEventMessage); ServiceMonitor.ServiceStatusSnapshot status = await Task.Run(() => _serviceMonitor.GetServiceStatus(serviceName)); _lastServiceRefresh = DateTime.MinValue; _lastEventLogRefresh = DateTime.MinValue; await RefreshAsync();
            if (!status.Found) return new VerificationResult("Unable to verify", status.Summary, false);
            if (status.IsRunning) return new VerificationResult("Service is running", $"{status.Summary} Sentinel AI will continue watching for another unexpected termination before marking the issue fully resolved.", true);
            return new VerificationResult("Service is still not running", $"{status.Summary} Do not change its startup type until you confirm whether the Windows feature that uses it is needed on this computer.", false);
        }

        private static void SuppressNonActionableStorageSpacesSmp(SystemSnapshot snapshot) { bool eventMatch = Contains(snapshot.LatestEventSource, "Service Control Manager") && Contains(snapshot.LatestEventMessage, "Microsoft Storage Spaces SMP"); if (!eventMatch) return; bool serviceMatch = Contains(snapshot.PrimaryFlaggedServiceName, "Storage Spaces") || Contains(snapshot.PrimaryFlaggedServiceName, "SMP"); snapshot.CriticalEventCount = 0; snapshot.ErrorEventCount = 0; snapshot.LatestEventTime = null; snapshot.LatestEventSource = "None"; snapshot.LatestEventMessage = "No actionable Windows events were detected."; if (serviceMatch) { snapshot.FlaggedServiceCount = 0; snapshot.PrimaryFlaggedServiceName = "None"; snapshot.PrimaryFlaggedServiceReason = "No actionable service conditions were detected."; } }
        private static void SuppressTransientWindowsUpdateFileInUse(SystemSnapshot snapshot) { bool match = Contains(snapshot.LatestEventSource, "WindowsUpdateClient") && Contains(snapshot.LatestEventMessage, "0x80073D02"); if (!match) return; snapshot.CriticalEventCount = 0; snapshot.ErrorEventCount = 0; snapshot.LatestEventTime = null; snapshot.LatestEventSource = "None"; snapshot.LatestEventMessage = "A temporary Windows Update file-in-use condition was detected. Windows can retry automatically; Sentinel will continue monitoring for recurrence."; }

        private async Task RefreshProcessDataIfDueAsync(DateTime now) { if (now - _lastProcessRefresh < ProcessRefreshInterval) return; _lastProcessRefresh = now; _processSnapshot = await Task.Run(_processMonitor.GetIntelligence); }
        private async Task RefreshMemoryInvestigationDataIfDueAsync(DateTime now, double memoryUsagePercent) { if (now - _lastMemoryInvestigationRefresh < MemoryInvestigationRefreshInterval) return; _lastMemoryInvestigationRefresh = now; _memoryInvestigationSnapshot = await Task.Run(() => _memoryInvestigationMonitor.GetSnapshot(memoryUsagePercent)); }
        private async Task RefreshProcessLineageDataIfDueAsync(DateTime now) { if (now - _lastProcessLineageRefresh < ProcessLineageRefreshInterval) return; _lastProcessLineageRefresh = now; _processLineageSnapshot = await Task.Run(_processLineageMonitor.GetSnapshot); }
        private async Task RefreshCommandLineDataIfDueAsync(DateTime now) { if (now - _lastCommandLineRefresh < CommandLineRefreshInterval) return; _lastCommandLineRefresh = now; _commandLineSnapshot = await Task.Run(_commandLineMonitor.GetSnapshot); }
        private async Task RefreshServiceDataIfDueAsync(DateTime now) { if (now - _lastServiceRefresh < ServiceRefreshInterval) return; _lastServiceRefresh = now; _serviceSnapshot = await Task.Run(_serviceMonitor.GetIntelligence); }
        private async Task RefreshSecurityDataIfDueAsync(DateTime now) { if (now - _lastSecurityRefresh < SecurityRefreshInterval) return; _lastSecurityRefresh = now; _securitySnapshot = await Task.Run(_securityMonitor.GetStatus); }
        private async Task RefreshEventLogDataIfDueAsync(DateTime now) { if (now - _lastEventLogRefresh < EventLogRefreshInterval) return; _lastEventLogRefresh = now; _eventLogSnapshot = await Task.Run(_eventLogMonitor.GetStatus); }
        private async Task RefreshStartupDataIfDueAsync(DateTime now) { if (now - _lastStartupRefresh < StartupRefreshInterval) return; _lastStartupRefresh = now; _startupSnapshot = await Task.Run(_startupPersistenceMonitor.GetSnapshot); }
        private async Task RefreshScheduledTaskDataIfDueAsync(DateTime now) { if (now - _lastScheduledTaskRefresh < ScheduledTaskRefreshInterval) return; _lastScheduledTaskRefresh = now; _scheduledTaskSnapshot = await Task.Run(_scheduledTaskMonitor.GetSnapshot); }
        private async Task RefreshActiveConnectionDataIfDueAsync(DateTime now) { if (now - _lastActiveConnectionRefresh < ActiveConnectionRefreshInterval) return; _lastActiveConnectionRefresh = now; _activeConnectionSnapshot = await Task.Run(_activeConnectionMonitor.GetSnapshot); }

        private static bool Contains(string? value, string expected) => !string.IsNullOrWhiteSpace(value) && value.Contains(expected, StringComparison.OrdinalIgnoreCase);
        private static string ExtractServiceDisplayName(string message) { const string prefix = "The "; const string marker = " service terminated unexpectedly"; int start = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase); int end = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase); if (start < 0 || end <= start + prefix.Length) return string.Empty; return message.Substring(start + prefix.Length, end - (start + prefix.Length)).Trim(); }
        public sealed record VerificationResult(string Title, string Summary, bool Resolved);
    }
}
