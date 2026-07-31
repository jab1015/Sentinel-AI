using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
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
        private bool _isRefreshing;
        private bool _initialRefreshStarted;
        private bool _wasAttentionActive;
        private bool _attentionNotificationActive;
        private string _guidanceActionId = string.Empty;
        private string _lastRecordedFingerprint = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            _timer.Interval = TimeSpan.FromSeconds(5);
            _timer.Tick += Timer_Tick;
            Activated += MainWindow_Activated;
        }

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_initialRefreshStarted) return;
            _initialRefreshStarted = true;
            Activated -= MainWindow_Activated;

            await EnsurePreferredNameAsync();
            await Task.Yield();
            _ = RunInitialRefreshAsync();
        }

        private async Task RunInitialRefreshAsync()
        {
            await Task.Delay(500);
            await UpdateDashboardAsync();
            _timer.Start();
        }

        private async Task EnsurePreferredNameAsync()
        {
            string preferredName = _userProfileService.GetPreferredName();
            if (string.IsNullOrWhiteSpace(preferredName))
            {
                string suggestedName = _userProfileService.GetSuggestedName();
                TextBox nameBox = new() { Text = suggestedName, PlaceholderText = "Your first name" };
                ContentDialog dialog = new()
                {
                    Title = "What should Sentinel call you?", Content = nameBox,
                    PrimaryButtonText = "Save", SecondaryButtonText = "Use Windows name",
                    DefaultButton = ContentDialogButton.Primary, XamlRoot = ((FrameworkElement)Content).XamlRoot
                };
                ContentDialogResult result = await dialog.ShowAsync();
                preferredName = result == ContentDialogResult.Primary ? nameBox.Text.Trim() : suggestedName;
                if (string.IsNullOrWhiteSpace(preferredName)) preferredName = suggestedName;
                _userProfileService.SavePreferredName(preferredName);
            }
            GreetingText.Text = $"Hello, {preferredName}.";
        }

        private async void Timer_Tick(object? sender, object e) => await UpdateDashboardAsync();

        private async Task UpdateDashboardAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            try
            {
                await _engine.RefreshAsync();
                var snapshot = _engine.CurrentSnapshot;

                CpuText.Text = $"CPU Usage: {snapshot.CpuUsagePercent:0.0}%";
                MemoryText.Text = $"Memory: {snapshot.MemoryUsedGB:0.00} GB / {snapshot.MemoryTotalGB:0.00} GB ({snapshot.MemoryUsagePercent:0.0}%)";
                double diskUsedGB = Math.Max(snapshot.DiskTotalGB - snapshot.DiskFreeGB, 0);
                DiskText.Text = snapshot.DiskTotalGB > 0 ? $"Disk: {diskUsedGB:0.00} GB / {snapshot.DiskTotalGB:0.00} GB ({snapshot.DiskUsagePercent:0.0}%)" : "Disk: Unavailable";
                NetworkText.Text = $"Network: ↓ {snapshot.DownloadMbps:0.00} Mbps   ↑ {snapshot.UploadMbps:0.00} Mbps";
                ProcessText.Text = snapshot.HighestMemoryProcessGB > 0 ? $"Processes: {snapshot.ProcessCount} running | Top memory: {snapshot.HighestMemoryProcessName} ({snapshot.HighestMemoryProcessGB:0.00} GB)" : $"Processes: {snapshot.ProcessCount} running";
                SecurityText.Text = $"Security: Defender {snapshot.DefenderStatus} | Firewall {snapshot.FirewallStatus}";

                CriticalEventsText.Text = snapshot.CriticalEventCount.ToString();
                ErrorEventsText.Text = snapshot.ErrorEventCount.ToString();
                LatestEventSummaryText.Text = snapshot.LatestEventTime.HasValue ? $"{snapshot.LatestEventTime.Value:MMM d, yyyy h:mm:ss tt} | {snapshot.LatestEventSource}" : "No recent critical or error events.";
                LatestEventMessageText.Text = snapshot.LatestEventMessage;
                RunningProcessesText.Text = snapshot.ProcessCount.ToString();
                FlaggedProcessesText.Text = snapshot.FlaggedProcessCount.ToString();
                PrimaryProcessText.Text = snapshot.FlaggedProcessCount > 0 ? snapshot.PrimaryFlaggedProcessName : "No process warning conditions were detected.";
                PrimaryProcessReasonText.Text = snapshot.PrimaryFlaggedProcessReason;
                InstalledServicesText.Text = snapshot.InstalledServiceCount.ToString();
                RunningServicesText.Text = snapshot.RunningServiceCount.ToString();
                FlaggedServicesText.Text = snapshot.FlaggedServiceCount.ToString();
                PrimaryServiceText.Text = snapshot.FlaggedServiceCount > 0 ? snapshot.PrimaryFlaggedServiceName : "No service warning conditions were detected.";
                PrimaryServiceReasonText.Text = snapshot.PrimaryFlaggedServiceReason;

                GuidanceTitleText.Text = snapshot.GuidanceTitle;
                GuidanceSeverityText.Text = snapshot.GuidanceSeverity;
                GuidanceConfidenceText.Text = $"{snapshot.GuidanceConfidencePercent}%";
                GuidanceConfidenceLabelText.Text = snapshot.GuidanceConfidenceLabel;
                GuidanceEvidenceText.Text = snapshot.GuidanceEvidence;
                GuidanceWhatHappenedText.Text = snapshot.GuidanceWhatHappened;
                GuidanceWhyItMattersText.Text = snapshot.GuidanceWhyItMatters;
                GuidanceActionText.Text = snapshot.GuidanceRecommendedAction;
                GuidanceFixAvailabilityText.Text = snapshot.GuidanceFixAvailability;
                GuidanceFixDetailsText.Text = snapshot.GuidanceFixDetails;
                _guidanceActionId = snapshot.GuidanceActionId;

                bool hasServiceFailure = snapshot.LatestEventSource.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase) && snapshot.LatestEventMessage.Contains("terminated unexpectedly", StringComparison.OrdinalIgnoreCase);
                bool isStorageSpacesSmpFinding = snapshot.LatestEventMessage.Contains("Storage Spaces SMP", StringComparison.OrdinalIgnoreCase) || snapshot.GuidanceTitle.Contains("Storage Spaces", StringComparison.OrdinalIgnoreCase) || snapshot.GuidanceWhatHappened.Contains("Storage Spaces", StringComparison.OrdinalIgnoreCase) || snapshot.PrimaryFlaggedServiceName.Contains("Storage Spaces", StringComparison.OrdinalIgnoreCase) || snapshot.PrimaryFlaggedServiceName.Contains("SMP", StringComparison.OrdinalIgnoreCase);
                bool memoryRequiresAttention = snapshot.MemoryPressureLevel.Equals("High", StringComparison.OrdinalIgnoreCase);
                bool hasSecurityOrProcessFinding = snapshot.FlaggedProcessCount > 0 || !snapshot.DefenderEnabled || !snapshot.FirewallEnabled;
                bool hasActionableServiceOrEventFinding = !isStorageSpacesSmpFinding && (snapshot.FlaggedServiceCount > 0 || snapshot.CriticalEventCount > 0 || snapshot.ErrorEventCount > 0 || hasServiceFailure || snapshot.RiskScore >= 20);
                bool requiresAttention = hasSecurityOrProcessFinding || hasActionableServiceOrEventFinding || memoryRequiresAttention;
                bool hasApprovalAction = snapshot.AutonomousProtectionRequiresUserApproval &&
                    !string.IsNullOrWhiteSpace(snapshot.AutonomousProtectionAction) &&
                    !snapshot.AutonomousProtectionAction.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(snapshot.AutonomousProtectionTarget) &&
                    !snapshot.AutonomousProtectionTarget.Equals("None", StringComparison.OrdinalIgnoreCase);

                await UpdateInvestigationHistoryAsync(snapshot, requiresAttention);
                UpdateBackgroundAttentionState(snapshot, requiresAttention);

                if (memoryRequiresAttention && !hasSecurityOrProcessFinding && !hasActionableServiceOrEventFinding)
                {
                    _guidanceActionId = "open-task-manager";
                    GuidanceActionButton.Content = "Review memory use";
                    GuidanceActionButton.Visibility = Visibility.Visible;
                    IssueSummaryBorder.Visibility = Visibility.Visible;

                    OverallStatusText.Text = "I found sustained high memory use that requires attention.";
                    AttentionStatusText.Text = snapshot.MemoryConclusion;
                    MonitoringStatusText.Text = snapshot.MemoryRecommendation;
                    RiskSummaryText.Text = $"Memory is at {snapshot.MemoryUsagePercent:0.0}%. Largest application contributors: {snapshot.MemoryTopContributors}. Windows Memory Compression is using {snapshot.MemoryCompressionGB:0.00} GB and should not be stopped.";
                    RecommendationText.Text = snapshot.MemoryRecommendation;
                }
                else
                {
                    if (hasApprovalAction)
                    {
                        _guidanceActionId = "approve-remediation";
                        GuidanceActionButton.Content = "Review recommended fix";
                        GuidanceActionButton.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        GuidanceActionButton.Content = snapshot.GuidanceActionLabel;
                        GuidanceActionButton.Visibility = requiresAttention && !string.IsNullOrWhiteSpace(_guidanceActionId) ? Visibility.Visible : Visibility.Collapsed;
                    }

                    IssueSummaryBorder.Visibility = requiresAttention ? Visibility.Visible : Visibility.Collapsed;

                    if (requiresAttention)
                    {
                        OverallStatusText.Text = "I analyzed your computer and found something that requires attention.";
                        AttentionStatusText.Text = "I investigated the available evidence and summarized what matters below.";
                        MonitoringStatusText.Text = hasApprovalAction
                            ? "I found a fix that requires your approval before Sentinel can make the change."
                            : "I’ll continue monitoring this condition and your computer.";
                        RiskSummaryText.Text = snapshot.RiskSummary;
                        RecommendationText.Text = snapshot.Recommendation;
                    }
                    else
                    {
                        OverallStatusText.Text = "Your computer is healthy.";
                        AttentionStatusText.Text = "Nothing requires your attention right now.";
                        MonitoringStatusText.Text = "I’ll continue monitoring your computer.";
                        RiskSummaryText.Text = "Your computer is healthy.";
                        RecommendationText.Text = "No action is required. Sentinel will continue monitoring your computer.";
                    }
                }

                VerifyGuidanceButton.Visibility = requiresAttention && hasServiceFailure && !isStorageSpacesSmpFinding ? Visibility.Visible : Visibility.Collapsed;
                RiskScoreText.Text = requiresAttention ? snapshot.RiskScore.ToString() : "0";
                RiskLevelText.Text = memoryRequiresAttention && !hasSecurityOrProcessFinding && !hasActionableServiceOrEventFinding ? "Memory Pressure" : requiresAttention ? $"{snapshot.RiskLevel} Risk" : "Healthy";
                LastUpdatedText.Text = $"Last Updated: {snapshot.Timestamp:hh:mm:ss tt}";
            }
            finally { _isRefreshing = false; }
        }

        private void UpdateBackgroundAttentionState(Models.SystemSnapshot snapshot, bool requiresAttention)
        {
            if (!requiresAttention)
            {
                if (_attentionNotificationActive)
                {
                    AppWindow.Title = "Sentinel AI";
                    _attentionNotificationActive = false;
                }
                return;
            }

            if (_attentionNotificationActive) return;

            string finding = !string.IsNullOrWhiteSpace(snapshot.GuidanceTitle) &&
                             !snapshot.GuidanceTitle.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? snapshot.GuidanceTitle
                : "Review recommended";

            AppWindow.Title = $"Sentinel AI — Attention: {finding}";
            _attentionNotificationActive = true;
        }

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
                    await _investigationHistoryService.RecordAsync(
                        _lastRecordedFingerprint,
                        "Condition resolved",
                        "Sentinel no longer detects the condition that previously required attention.",
                        "Resolved",
                        requiresAttention: false,
                        resolved: true);
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

            if (!fingerprint.Equals(_lastRecordedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                var recent = await _investigationHistoryService.ReadRecentAsync(50);
                var previous = recent.FirstOrDefault(entry =>
                    entry.RequiresAttention &&
                    string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));

                if (previous is not null)
                {
                    HistoryOutcomeIconText.Text = "↻";
                    HistoryTitleText.Text = "Sentinel has seen this condition before";
                    HistorySummaryText.Text = string.IsNullOrWhiteSpace(previous.Conclusion)
                        ? snapshot.InvestigationSummary
                        : previous.Conclusion;
                    HistoryOutcomeText.Text = $"Previously investigated {previous.TimestampUtc.ToLocalTime():MMM d, yyyy h:mm tt}. Sentinel is comparing the current evidence with that earlier occurrence.";
                    InvestigationHistoryBorder.Visibility = Visibility.Visible;
                }
                else
                {
                    InvestigationHistoryBorder.Visibility = Visibility.Collapsed;
                }

                await _investigationHistoryService.RecordAsync(
                    fingerprint,
                    string.IsNullOrWhiteSpace(snapshot.GuidanceTitle) ? "Investigation" : snapshot.GuidanceTitle,
                    string.IsNullOrWhiteSpace(snapshot.InvestigationSummary) ? snapshot.GuidanceWhatHappened : snapshot.InvestigationSummary,
                    snapshot.GuidanceSeverity,
                    requiresAttention: true,
                    resolved: false);

                _lastRecordedFingerprint = fingerprint;
            }
        }

        private async void GuidanceActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_guidanceActionId == "open-task-manager")
            {
                Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                return;
            }

            if (_guidanceActionId == "approve-remediation")
            {
                await ReviewApprovedRemediationAsync();
                return;
            }

            if (_guidanceActionId == "open-windows-update")
            {
                Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });
            }
        }

        private async Task ReviewApprovedRemediationAsync()
        {
            var snapshot = _engine.CurrentSnapshot;
            RemediationApprovalCoordinator.ApprovalRequest request = _approvalCoordinator.CreateRequest(snapshot);
            if (!request.Available) return;

            ContentDialog dialog = new()
            {
                Title = request.Title,
                Content = $"{request.Summary}\n\nTarget: {request.Target}\n\n{request.SafetyNote}",
                PrimaryButtonText = "Approve",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var execution = await _approvedServiceRestartCoordinator.ExecuteAsync(
                snapshot,
                request,
                async () => await _engine.RefreshAsync());

            ContentDialog outcomeDialog = new()
            {
                Title = execution.Title,
                Content = execution.Summary,
                CloseButtonText = "OK",
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };
            await outcomeDialog.ShowAsync();
            await UpdateDashboardAsync();
        }

        private async void VerifyGuidanceButton_Click(object sender, RoutedEventArgs e)
        {
            var result = await _engine.VerifyCurrentGuidanceAsync();
            ContentDialog dialog = new()
            {
                Title = result.Title,
                Content = result.Message,
                CloseButtonText = "OK",
                XamlRoot = ((FrameworkElement)Content).XamlRoot
            };
            await dialog.ShowAsync();
            await UpdateDashboardAsync();
        }
    }
}
