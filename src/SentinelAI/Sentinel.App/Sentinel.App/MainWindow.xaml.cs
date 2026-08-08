using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Sentinel.App.Models;
using Sentinel.App.Services;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Sentinel.App
{
    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherTimer _timer = new();
        private readonly MonitoringEngine _engine = new();
        private readonly UserProfileService _userProfileService = new();
        private readonly RemediationApprovalCoordinator _approvalCoordinator = new();
        private readonly ApprovedServiceRestartCoordinator _approvedServiceRestartCoordinator = new();
        private readonly InvestigationHistoryService _investigationHistoryService = new();
        private readonly MaintenanceOutcomeRecorder _monitoringOutcomeRecorder = new();
        private readonly AutomaticOptimizationCoordinator _automaticOptimizationCoordinator = new();
        private readonly IntegratedMaintenanceCoordinator _integratedMaintenanceCoordinator = new();
        private readonly LivePersistentExceptionCoordinator _livePersistentExceptionCoordinator = new();
        private readonly AdaptiveDiscoveryCadenceService _adaptiveDiscoveryCadenceService = new();
        private readonly LiveEventDrivenDiscoveryCoordinator _liveEventDrivenDiscoveryCoordinator = new();
        private bool _isRefreshing;
        private bool _initialRefreshStarted;
        private bool _profileInitialized;
        private bool _wasAttentionActive;
        private bool _attentionNotificationActive;
        private bool _eventDrivenFollowUpPending;
        private string _guidanceActionId = string.Empty;
        private string _lastRecordedFingerprint = string.Empty;
        private PersistentInvestigationRecord? _currentPersistentException;

        public MainWindow()
        {
            InitializeComponent();
            _timer.Interval = TimeSpan.FromSeconds(5);
            _timer.Tick += Timer_Tick;
            Activated += MainWindow_Activated;
        }

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (!_profileInitialized)
            {
                _profileInitialized = true;
                await EnsurePreferredNameAsync();
            }

            StartMonitoringIfNeeded();
        }

        public void StartBackgroundMonitoring() => StartMonitoringIfNeeded();

        private void StartMonitoringIfNeeded()
        {
            if (_initialRefreshStarted) return;
            _initialRefreshStarted = true;
            ShowInitialDiscoveryState();
            _ = RunInitialRefreshAsync();
        }

        private void ShowInitialDiscoveryState()
        {
            OverallStatusText.Text = "Sentinel is checking your computer.";
            AttentionStatusText.Text = "Gathering current Windows, security, driver, process, service, network, and system-health information…";
            MonitoringStatusText.Text = "I’ll show you the results as soon as this initial check is complete.";
            GuidanceActionButton.Visibility = Visibility.Collapsed;
            IssueSummaryBorder.Visibility = Visibility.Collapsed;
            AppWindow.Title = "Sentinel AI — Checking your computer…";
        }

        private async Task RunInitialRefreshAsync()
        {
            await Task.Delay(250);
            await UpdateDashboardAsync();
            _timer.Start();
        }

        private async Task EnsurePreferredNameAsync()
        {
            string preferredName = _userProfileService.GetPreferredName();
            if (string.IsNullOrWhiteSpace(preferredName))
            {
                FrameworkElement rootElement = (FrameworkElement)Content;
                await WaitForXamlRootAsync(rootElement);
                string suggestedName = _userProfileService.GetSuggestedName();
                TextBox nameBox = new() { Text = suggestedName, PlaceholderText = "Your first name" };
                ContentDialog dialog = new()
                {
                    Title = "What should Sentinel call you?", Content = nameBox, PrimaryButtonText = "Save",
                    SecondaryButtonText = "Use Windows name", DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = rootElement.XamlRoot
                };
                ContentDialogResult result = await dialog.ShowAsync();
                preferredName = result == ContentDialogResult.Primary ? nameBox.Text.Trim() : suggestedName;
                if (string.IsNullOrWhiteSpace(preferredName)) preferredName = suggestedName;
                _userProfileService.SavePreferredName(preferredName);
            }
            GreetingText.Text = $"Hello, {preferredName}.";
        }

        private static Task WaitForXamlRootAsync(FrameworkElement rootElement)
        {
            if (rootElement.XamlRoot is not null) return Task.CompletedTask;
            TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler? loadedHandler = null;
            loadedHandler = (sender, args) =>
            {
                rootElement.Loaded -= loadedHandler;
                completion.TrySetResult(true);
            };
            rootElement.Loaded += loadedHandler;
            return completion.Task;
        }

        private async void Timer_Tick(object? sender, object e) => await UpdateDashboardAsync();

        private async Task UpdateDashboardAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            bool forceEventDrivenFollowUp = false;
            try
            {
                await _engine.RefreshAsync();
                var snapshot = _engine.CurrentSnapshot;
                LivePersistentExceptionCoordinator.LivePersistentExceptionResult persistentException = await _livePersistentExceptionCoordinator.EvaluateAsync(snapshot);
                _currentPersistentException = persistentException.Record;
                LiveEventDrivenDiscoveryCoordinator.LiveEventDrivenDecision eventDrivenDecision = _liveEventDrivenDiscoveryCoordinator.Evaluate(snapshot, persistentException.SuppressNotification);
                forceEventDrivenFollowUp = eventDrivenDecision.ForceImmediateRecheck;
                AdaptiveDiscoveryCadenceService.AdaptiveDiscoveryDecision cadence = _adaptiveDiscoveryCadenceService.Evaluate(snapshot, suppressCurrentInvestigation: persistentException.SuppressNotification);
                if (_timer.Interval != cadence.NextCheckInterval) _timer.Interval = cadence.NextCheckInterval;

                CpuText.Text = $"CPU Usage: {snapshot.CpuUsagePercent:0.0}%";
                MemoryText.Text = $"Physical Memory: {snapshot.MemoryUsedGB:0.00} GB / {snapshot.MemoryTotalGB:0.00} GB ({snapshot.MemoryUsagePercent:0.0}%)";
                double diskUsedGB = Math.Max(snapshot.DiskTotalGB - snapshot.DiskFreeGB, 0);
                DiskText.Text = snapshot.DiskTotalGB > 0 ? $"Windows System Drive: {diskUsedGB:0.00} GB used / {snapshot.DiskTotalGB:0.00} GB ({snapshot.DiskUsagePercent:0.0}%)" : "Windows System Drive: Unavailable";
                NetworkText.Text = $"Current Network Activity: ↓ {snapshot.DownloadMbps:0.00} Mbps   ↑ {snapshot.UploadMbps:0.00} Mbps";
                ProcessText.Text = snapshot.HighestMemoryProcessGB > 0 ? $"Running Processes: {snapshot.ProcessCount} | Highest working memory: {snapshot.HighestMemoryProcessName} ({snapshot.HighestMemoryProcessGB:0.00} GB)" : $"Running Processes: {snapshot.ProcessCount}";
                SecurityText.Text = $"Windows Security Evidence: Defender {FormatSecurityEvidence(snapshot.DefenderStatus)} | Firewall {FormatSecurityEvidence(snapshot.FirewallStatus)}";
                CriticalEventsText.Text = snapshot.CriticalEventCount.ToString(); ErrorEventsText.Text = snapshot.ErrorEventCount.ToString();
                LatestEventSummaryText.Text = snapshot.LatestEventTime.HasValue ? $"{snapshot.LatestEventTime.Value:MMM d, yyyy h:mm:ss tt} | {snapshot.LatestEventSource}" : "No recent critical or error events.";
                LatestEventMessageText.Text = snapshot.LatestEventMessage;
                RunningProcessesText.Text = snapshot.ProcessCount.ToString(); FlaggedProcessesText.Text = snapshot.FlaggedProcessCount.ToString();
                PrimaryProcessText.Text = snapshot.FlaggedProcessCount > 0 ? snapshot.PrimaryFlaggedProcessName : "No process warning conditions were detected."; PrimaryProcessReasonText.Text = snapshot.PrimaryFlaggedProcessReason;
                InstalledServicesText.Text = snapshot.InstalledServiceCount.ToString(); RunningServicesText.Text = snapshot.RunningServiceCount.ToString(); FlaggedServicesText.Text = snapshot.FlaggedServiceCount.ToString();
                PrimaryServiceText.Text = snapshot.FlaggedServiceCount > 0 ? snapshot.PrimaryFlaggedServiceName : "No service warning conditions were detected."; PrimaryServiceReasonText.Text = snapshot.PrimaryFlaggedServiceReason;
                GuidanceTitleText.Text = snapshot.GuidanceTitle; GuidanceSeverityText.Text = snapshot.GuidanceSeverity; GuidanceConfidenceText.Text = $"{snapshot.GuidanceConfidencePercent}%"; GuidanceConfidenceLabelText.Text = snapshot.GuidanceConfidenceLabel; GuidanceEvidenceText.Text = snapshot.GuidanceEvidence; GuidanceWhatHappenedText.Text = snapshot.GuidanceWhatHappened; GuidanceWhyItMattersText.Text = snapshot.GuidanceWhyItMatters; GuidanceActionText.Text = snapshot.GuidanceRecommendedAction; GuidanceFixAvailabilityText.Text = snapshot.GuidanceFixAvailability; GuidanceFixDetailsText.Text = snapshot.GuidanceFixDetails; _guidanceActionId = snapshot.GuidanceActionId;

                if (persistentException.ShowKnownCondition && persistentException.Decision is not null)
                {
                    GuidanceTitleText.Text = persistentException.Decision.Title; GuidanceSeverityText.Text = "Known condition"; GuidanceConfidenceText.Text = $"{persistentException.Record?.ConfidencePercent ?? 0}%"; GuidanceConfidenceLabelText.Text = persistentException.Record?.TrustLevel ?? "Verified investigation memory"; GuidanceEvidenceText.Text = persistentException.Record?.EvidenceSummary ?? persistentException.Decision.Summary; GuidanceWhatHappenedText.Text = persistentException.Decision.Summary; GuidanceWhyItMattersText.Text = "Sentinel verified that this exact noncritical condition has no remaining safe repair path at this time."; GuidanceActionText.Text = persistentException.SuppressNotification ? "Sentinel is monitoring this exact condition silently and will reopen it if material evidence changes." : "You may ask Sentinel to monitor this exact condition silently. Monitoring will continue either way."; GuidanceFixAvailabilityText.Text = "Verified persistent exception"; GuidanceFixDetailsText.Text = "Notification suppression does not disable Discovery or background monitoring."; _guidanceActionId = persistentException.SuppressNotification ? "resume-persistent-notifications" : "monitor-persistent-silently";
                }

                bool hasServiceFailure = snapshot.LatestEventSource.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase) && snapshot.LatestEventMessage.Contains("terminated unexpectedly", StringComparison.OrdinalIgnoreCase);
                bool isStorageSpacesSmpFinding = snapshot.LatestEventMessage.Contains("Storage Spaces SMP", StringComparison.OrdinalIgnoreCase) || snapshot.GuidanceTitle.Contains("Storage Spaces", StringComparison.OrdinalIgnoreCase) || snapshot.GuidanceWhatHappened.Contains("Storage Spaces", StringComparison.OrdinalIgnoreCase) || snapshot.PrimaryFlaggedServiceName.Contains("Storage Spaces", StringComparison.OrdinalIgnoreCase) || snapshot.PrimaryFlaggedServiceName.Contains("SMP", StringComparison.OrdinalIgnoreCase);
                bool resolvedProcessReview = snapshot.FlaggedProcessCount > 0 && snapshot.GuidanceFixAvailability.Equals("No fix needed", StringComparison.OrdinalIgnoreCase) && snapshot.GuidanceTitle.Contains("no security risk found", StringComparison.OrdinalIgnoreCase);
                bool memoryRequiresAttention = snapshot.MemoryPressureLevel.Equals("High", StringComparison.OrdinalIgnoreCase);
                bool investigationRequiresAttention = snapshot.InvestigationRequiresAttention && !persistentException.SuppressNotification;
                bool hasApprovalAction = snapshot.AutonomousProtectionRequiresUserApproval && !string.IsNullOrWhiteSpace(snapshot.AutonomousProtectionAction) && !snapshot.AutonomousProtectionAction.Equals("None", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(snapshot.AutonomousProtectionTarget) && !snapshot.AutonomousProtectionTarget.Equals("None", StringComparison.OrdinalIgnoreCase);
                bool requiresAttention = investigationRequiresAttention || hasApprovalAction || memoryRequiresAttention;
                await UpdateInvestigationHistoryAsync(snapshot, investigationRequiresAttention); UpdateBackgroundAttentionState(snapshot, requiresAttention);

                if (persistentException.SuppressNotification) { GuidanceActionButton.Visibility = Visibility.Collapsed; IssueSummaryBorder.Visibility = Visibility.Collapsed; OverallStatusText.Text = "Your computer is healthy."; AttentionStatusText.Text = "Nothing requires your attention right now."; MonitoringStatusText.Text = "Sentinel is also monitoring a known noncritical condition silently."; RiskSummaryText.Text = "A previously investigated noncritical condition is unchanged and remains under background monitoring."; RecommendationText.Text = "No action is required. Sentinel will notify you if the condition or available repair evidence changes."; }
                else if (memoryRequiresAttention && !investigationRequiresAttention && !hasApprovalAction) { _guidanceActionId = "open-task-manager"; GuidanceActionButton.Content = "Review memory use"; GuidanceActionButton.Visibility = Visibility.Visible; IssueSummaryBorder.Visibility = Visibility.Visible; OverallStatusText.Text = "I found sustained high memory use that requires attention."; AttentionStatusText.Text = snapshot.MemoryConclusion; MonitoringStatusText.Text = snapshot.MemoryRecommendation; RiskSummaryText.Text = $"Memory is at {snapshot.MemoryUsagePercent:0.0}%. Largest application contributors: {snapshot.MemoryTopContributors}. Windows Memory Compression is using {snapshot.MemoryCompressionGB:0.00} GB and should not be stopped."; RecommendationText.Text = snapshot.MemoryRecommendation; }
                else
                {
                    if (persistentException.ShowKnownCondition && persistentException.CanToggleNotifications) { GuidanceActionButton.Content = persistentException.Decision?.ActionLabel ?? "Monitor Silently"; GuidanceActionButton.Visibility = Visibility.Visible; }
                    else if (hasApprovalAction) { _guidanceActionId = "approve-remediation"; GuidanceActionButton.Content = "Review recommended fix"; GuidanceActionButton.Visibility = Visibility.Visible; }
                    else { GuidanceActionButton.Content = snapshot.GuidanceActionLabel; GuidanceActionButton.Visibility = investigationRequiresAttention && !string.IsNullOrWhiteSpace(_guidanceActionId) ? Visibility.Visible : Visibility.Collapsed; }
                    IssueSummaryBorder.Visibility = (investigationRequiresAttention || hasApprovalAction) ? Visibility.Visible : Visibility.Collapsed;
                    if (investigationRequiresAttention || hasApprovalAction) { OverallStatusText.Text = persistentException.ShowKnownCondition ? "Sentinel recognizes a previously investigated condition." : "I analyzed your computer and found something that requires attention."; AttentionStatusText.Text = persistentException.ShowKnownCondition ? "The condition is unchanged and has no remaining verified safe repair path." : "I investigated the available evidence and summarized what matters below."; MonitoringStatusText.Text = hasApprovalAction ? "I found a fix that requires your approval before Sentinel can make the change." : "I’ll continue monitoring this condition and your computer."; RiskSummaryText.Text = persistentException.ShowKnownCondition ? "This is a verified persistent noncritical condition." : snapshot.RiskSummary; RecommendationText.Text = persistentException.ShowKnownCondition ? "Choose Monitor Silently to hide repeated reminders while Sentinel continues watching for meaningful changes." : snapshot.Recommendation; }
                    else if (resolvedProcessReview) { OverallStatusText.Text = "Your computer is healthy."; AttentionStatusText.Text = "Sentinel checked the unusual activity and found no security risk."; MonitoringStatusText.Text = "No action is needed. I’ll continue monitoring your computer."; RiskSummaryText.Text = "Sentinel completed the investigation and found no security risk."; RecommendationText.Text = "No action is required. Sentinel will continue monitoring automatically."; }
                    else { OverallStatusText.Text = "Your computer is healthy."; AttentionStatusText.Text = "Nothing requires your attention right now."; MonitoringStatusText.Text = "I’ll continue monitoring your computer."; RiskSummaryText.Text = "Your computer is healthy."; RecommendationText.Text = "No action is required. Sentinel will continue monitoring your computer."; }
                }
                VerifyGuidanceButton.Visibility = investigationRequiresAttention && hasServiceFailure && !isStorageSpacesSmpFinding ? Visibility.Visible : Visibility.Collapsed;
                RiskScoreText.Text = requiresAttention ? snapshot.RiskScore.ToString() : "0"; RiskLevelText.Text = memoryRequiresAttention && !investigationRequiresAttention && !hasApprovalAction ? "Memory Pressure" : requiresAttention ? $"{snapshot.RiskLevel} Risk" : "Healthy";
                LastUpdatedText.Text = $"Evidence Collected: {snapshot.Timestamp:MMM d, yyyy h:mm:ss tt}";
                AutomaticOptimizationResult optimization = await _automaticOptimizationCoordinator.EvaluateAndRunAsync(snapshot);
                UpdateOptimizationStatus(optimization);
                _ = _integratedMaintenanceCoordinator.EvaluateAndRunAsync();
            }
            finally { _isRefreshing = false; }
            if (forceEventDrivenFollowUp) ScheduleEventDrivenFollowUp();
        }

        private static string FormatSecurityEvidence(string status) => status switch
        {
            "Enabled" => "appears enabled",
            "Limited" => "appears limited",
            "Disabled" => "appears disabled",
            "Disabled or inactive" => "appears disabled or inactive",
            "Not detected" => "not detected",
            "Unavailable" => "could not be verified",
            _ => status
        };

        private void ScheduleEventDrivenFollowUp() { if (_eventDrivenFollowUpPending) return; _eventDrivenFollowUpPending = true; _ = RunEventDrivenFollowUpAsync(); }
        private async Task RunEventDrivenFollowUpAsync() { try { await Task.Delay(250); await UpdateDashboardAsync(); } finally { _eventDrivenFollowUpPending = false; } }
        private void UpdateBackgroundAttentionState(Models.SystemSnapshot snapshot, bool requiresAttention) { if (!requiresAttention) { AppWindow.Title = "Sentinel AI"; _attentionNotificationActive = false; return; } if (_attentionNotificationActive) return; string finding = !string.IsNullOrWhiteSpace(snapshot.GuidanceTitle) && !snapshot.GuidanceTitle.Equals("None", StringComparison.OrdinalIgnoreCase) ? snapshot.GuidanceTitle : "Review recommended"; AppWindow.Title = $"Sentinel AI — Attention: {finding}"; _attentionNotificationActive = true; }
        private async Task UpdateInvestigationHistoryAsync(Models.SystemSnapshot snapshot, bool requiresAttention)
        {
            string fingerprint = snapshot.InvestigationReasonCode?.Trim() ?? string.Empty;
            bool validFingerprint = !string.IsNullOrWhiteSpace(fingerprint) &&
                !fingerprint.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                !fingerprint.Equals("Healthy", StringComparison.OrdinalIgnoreCase);

            if (!requiresAttention)
            {
                InvestigationHistoryBorder.Visibility = Visibility.Collapsed;
                if (_wasAttentionActive && !string.IsNullOrWhiteSpace(_lastRecordedFingerprint))
                {
                    const string resolvedSummary = "Sentinel no longer detects the condition that previously required attention.";
                    await _investigationHistoryService.RecordAsync(_lastRecordedFingerprint, "Condition resolved", resolvedSummary, "Resolved", false, true);
                    _monitoringOutcomeRecorder.RecordVerificationResult("Condition resolved", resolvedSummary, true, $"Fingerprint: {_lastRecordedFingerprint}");
                    UpdateMaintenanceReport();
                }
                _wasAttentionActive = false;
                _lastRecordedFingerprint = string.Empty;
                return;
            }

            _wasAttentionActive = true;
            if (!validFingerprint)
            {
                InvestigationHistoryBorder.Visibility = Visibility.Collapsed;
                return;
            }

            if (fingerprint.Equals(_lastRecordedFingerprint, StringComparison.OrdinalIgnoreCase)) return;

            var recent = await _investigationHistoryService.ReadRecentAsync(50);
            var previous = recent.FirstOrDefault(entry => entry.RequiresAttention &&
                string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            string title = string.IsNullOrWhiteSpace(snapshot.GuidanceTitle) ? "Investigation" : snapshot.GuidanceTitle;
            string summary = string.IsNullOrWhiteSpace(snapshot.InvestigationSummary) ? snapshot.GuidanceWhatHappened : snapshot.InvestigationSummary;

            if (previous is not null)
            {
                HistoryOutcomeIconText.Text = "↻";
                HistoryTitleText.Text = "Sentinel has seen this condition before";
                HistorySummaryText.Text = string.IsNullOrWhiteSpace(previous.Conclusion) ? summary : previous.Conclusion;
                HistoryOutcomeText.Text = $"Previously investigated {previous.TimestampUtc.ToLocalTime():MMM d, yyyy h:mm tt}. Sentinel is comparing the current evidence with that earlier occurrence.";
                InvestigationHistoryBorder.Visibility = Visibility.Visible;
            }
            else
            {
                InvestigationHistoryBorder.Visibility = Visibility.Collapsed;
            }

            await _investigationHistoryService.RecordAsync(fingerprint, title, summary, snapshot.GuidanceSeverity, true, false);
            _monitoringOutcomeRecorder.RecordInvestigation(title, summary, true, $"Fingerprint: {fingerprint}; Recurring: {previous is not null}");
            UpdateMaintenanceReport();
            _lastRecordedFingerprint = fingerprint;
        }
        private async void GuidanceActionButton_Click(object sender, RoutedEventArgs e) { switch (_guidanceActionId) { case "open-task-manager": OpenShellTarget("taskmgr.exe"); return; case "approve-remediation": await ReviewApprovedRemediationAsync(); return; case "review-driver-repair": AskSentinelQuestionBox.Text = "Do I have any driver conflicts?"; await SubmitAskSentinelQuestionAsync(); return; case "open-windows-update": OpenShellTarget("ms-settings:windowsupdate"); return; case "open-windows-security": OpenShellTarget("windowsdefender:"); return; case "open-firewall": OpenShellTarget("windowsdefender://network"); return; case "open-services": OpenShellTarget("services.msc"); return; case "open-storage": OpenShellTarget("ms-settings:storagesense"); return; case "check-again": await UpdateDashboardAsync(); return; case "monitor-persistent-silently": await SetPersistentNotificationStateAsync(true); return; case "resume-persistent-notifications": await SetPersistentNotificationStateAsync(false); return; } }
        private async Task SetPersistentNotificationStateAsync(bool suppress) { if (_currentPersistentException is null) return; PersistentInvestigationMemoryService.SuppressionDecision result = await _livePersistentExceptionCoordinator.SetSilentMonitoringAsync(_currentPersistentException, suppress); _askSentinelOutcomeRecorder.RecordInvestigation(suppress ? "Known condition monitoring" : "Known condition notifications resumed", result.Message, result.Allowed, $"Investigation: {_currentPersistentException.InvestigationId}; Fingerprint: {_currentPersistentException.Fingerprint}; Monitoring continues: true"); UpdateMaintenanceReport(); ContentDialog dialog = new() { Title = result.Allowed ? suppress ? "Monitoring silently" : "Notifications resumed" : "Sentinel cannot change this notification", Content = result.Message, CloseButtonText = "OK", XamlRoot = ((FrameworkElement)Content).XamlRoot }; await dialog.ShowAsync(); await UpdateDashboardAsync(); }
        private static void OpenShellTarget(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { } }
        private async Task ReviewApprovedRemediationAsync() { var snapshot = _engine.CurrentSnapshot; RemediationApprovalCoordinator.RemediationApprovalRequest? request = _approvalCoordinator.CreateRequest(snapshot); if (request is null) return; ContentDialog dialog = new() { Title = request.Title, Content = $"{request.Summary}\n\nTarget: {request.Target}\n\nSentinel will revalidate the investigation immediately before acting. This approval is single-use and expires automatically.", PrimaryButtonText = "Approve", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = ((FrameworkElement)Content).XamlRoot }; ContentDialogResult result = await dialog.ShowAsync(); if (result != ContentDialogResult.Primary) return; await _engine.RefreshAsync(); var currentSnapshot = _engine.CurrentSnapshot; RemediationApprovalCoordinator.ApprovalValidationResult validation = _approvalCoordinator.Validate(request, currentSnapshot, true); var execution = await _approvedServiceRestartCoordinator.ExecuteAsync(currentSnapshot, request, validation, true); ContentDialog outcomeDialog = new() { Title = string.IsNullOrWhiteSpace(execution.Title) ? "Sentinel did not make a change" : execution.Title, Content = execution.Summary, CloseButtonText = "OK", XamlRoot = ((FrameworkElement)Content).XamlRoot }; await outcomeDialog.ShowAsync(); await UpdateDashboardAsync(); }
        private async void VerifyGuidanceButton_Click(object sender, RoutedEventArgs e) { var result = await _engine.VerifyCurrentGuidanceAsync(); ContentDialog dialog = new() { Title = result.Title, Content = result.Summary, CloseButtonText = "OK", XamlRoot = ((FrameworkElement)Content).XamlRoot }; await dialog.ShowAsync(); await UpdateDashboardAsync(); }
    }
}
