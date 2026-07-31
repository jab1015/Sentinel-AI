using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Sentinel.App.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Sentinel.App
{
    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherTimer _timer = new();
        private readonly MonitoringEngine _engine = new();
        private bool _isRefreshing;
        private bool _initialRefreshStarted;
        private string _guidanceActionId = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;

            Activated += MainWindow_Activated;
        }

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_initialRefreshStarted)
            {
                return;
            }

            _initialRefreshStarted = true;
            Activated -= MainWindow_Activated;

            // Allow the window to paint before starting the first investigation pass.
            await Task.Delay(150);
            await UpdateDashboardAsync();
            _timer.Start();
        }

        private async void Timer_Tick(object? sender, object e)
        {
            await UpdateDashboardAsync();
        }

        private async Task UpdateDashboardAsync()
        {
            if (_isRefreshing)
            {
                return;
            }

            _isRefreshing = true;
            try
            {
                await _engine.RefreshAsync();
                var snapshot = _engine.CurrentSnapshot;

                CpuText.Text = $"CPU Usage: {snapshot.CpuUsagePercent:0.0}%";
                MemoryText.Text =
                    $"Memory: {snapshot.MemoryUsedGB:0.00} GB / {snapshot.MemoryTotalGB:0.00} GB ({snapshot.MemoryUsagePercent:0.0}%)";

                double diskUsedGB = Math.Max(snapshot.DiskTotalGB - snapshot.DiskFreeGB, 0);
                DiskText.Text = snapshot.DiskTotalGB > 0
                    ? $"Disk: {diskUsedGB:0.00} GB / {snapshot.DiskTotalGB:0.00} GB ({snapshot.DiskUsagePercent:0.0}%)"
                    : "Disk: Unavailable";

                NetworkText.Text =
                    $"Network: ↓ {snapshot.DownloadMbps:0.00} Mbps   ↑ {snapshot.UploadMbps:0.00} Mbps";
                ProcessText.Text = snapshot.HighestMemoryProcessGB > 0
                    ? $"Processes: {snapshot.ProcessCount} running | Top memory: {snapshot.HighestMemoryProcessName} ({snapshot.HighestMemoryProcessGB:0.00} GB)"
                    : $"Processes: {snapshot.ProcessCount} running";
                SecurityText.Text =
                    $"Security: Defender {snapshot.DefenderStatus} | Firewall {snapshot.FirewallStatus}";

                CriticalEventsText.Text = snapshot.CriticalEventCount.ToString();
                ErrorEventsText.Text = snapshot.ErrorEventCount.ToString();
                LatestEventSummaryText.Text = snapshot.LatestEventTime.HasValue
                    ? $"{snapshot.LatestEventTime.Value:MMM d, yyyy h:mm:ss tt} | {snapshot.LatestEventSource}"
                    : "No recent critical or error events.";
                LatestEventMessageText.Text = snapshot.LatestEventMessage;

                RunningProcessesText.Text = snapshot.ProcessCount.ToString();
                FlaggedProcessesText.Text = snapshot.FlaggedProcessCount.ToString();
                PrimaryProcessText.Text = snapshot.FlaggedProcessCount > 0
                    ? snapshot.PrimaryFlaggedProcessName
                    : "No process warning conditions were detected.";
                PrimaryProcessReasonText.Text = snapshot.PrimaryFlaggedProcessReason;

                InstalledServicesText.Text = snapshot.InstalledServiceCount.ToString();
                RunningServicesText.Text = snapshot.RunningServiceCount.ToString();
                FlaggedServicesText.Text = snapshot.FlaggedServiceCount.ToString();
                PrimaryServiceText.Text = snapshot.FlaggedServiceCount > 0
                    ? snapshot.PrimaryFlaggedServiceName
                    : "No service warning conditions were detected.";
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

                bool hasServiceFailure =
                    snapshot.LatestEventSource.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase) &&
                    snapshot.LatestEventMessage.Contains("terminated unexpectedly", StringComparison.OrdinalIgnoreCase);

                bool isStorageSpacesSmpFinding =
                    snapshot.LatestEventMessage.Contains("Storage Spaces SMP", StringComparison.OrdinalIgnoreCase) ||
                    snapshot.GuidanceTitle.Contains("Storage Spaces", StringComparison.OrdinalIgnoreCase) ||
                    snapshot.GuidanceWhatHappened.Contains("Storage Spaces", StringComparison.OrdinalIgnoreCase) ||
                    snapshot.PrimaryFlaggedServiceName.Contains("Storage Spaces", StringComparison.OrdinalIgnoreCase) ||
                    snapshot.PrimaryFlaggedServiceName.Contains("SMP", StringComparison.OrdinalIgnoreCase);

                bool hasSecurityOrProcessFinding =
                    snapshot.FlaggedProcessCount > 0 ||
                    !snapshot.DefenderEnabled ||
                    !snapshot.FirewallEnabled;

                bool hasActionableServiceOrEventFinding =
                    !isStorageSpacesSmpFinding &&
                    (snapshot.FlaggedServiceCount > 0 ||
                     snapshot.CriticalEventCount > 0 ||
                     snapshot.ErrorEventCount > 0 ||
                     hasServiceFailure ||
                     snapshot.RiskScore >= 20);

                bool requiresAttention =
                    hasSecurityOrProcessFinding || hasActionableServiceOrEventFinding;

                GuidanceActionButton.Content = snapshot.GuidanceActionLabel;
                GuidanceActionButton.Visibility = requiresAttention && !string.IsNullOrWhiteSpace(_guidanceActionId)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                IssueSummaryBorder.Visibility = requiresAttention ? Visibility.Visible : Visibility.Collapsed;
                InvestigationHistoryBorder.Visibility = Visibility.Collapsed;

                if (requiresAttention)
                {
                    OverallStatusText.Text = "I analyzed your computer and found something that requires attention.";
                    AttentionStatusText.Text = "I investigated the available evidence and summarized what matters below.";
                    MonitoringStatusText.Text = "I’ll continue monitoring this condition and your computer.";
                }
                else
                {
                    OverallStatusText.Text = "Your computer is healthy.";
                    AttentionStatusText.Text = "Nothing requires your attention right now.";
                    MonitoringStatusText.Text = "I’ll continue monitoring your computer.";
                }

                VerifyGuidanceButton.Visibility = requiresAttention && hasServiceFailure && !isStorageSpacesSmpFinding
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                RiskScoreText.Text = requiresAttention ? snapshot.RiskScore.ToString() : "0";
                RiskLevelText.Text = requiresAttention ? $"{snapshot.RiskLevel} Risk" : "Healthy";
                RiskSummaryText.Text = requiresAttention
                    ? snapshot.RiskSummary
                    : "Your computer is healthy.";
                RecommendationText.Text = requiresAttention
                    ? snapshot.Recommendation
                    : "No action is required. Sentinel will continue monitoring your computer.";
                LastUpdatedText.Text = $"Last Updated: {snapshot.Timestamp:hh:mm:ss tt}";
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private async void GuidanceActionButton_Click(object sender, RoutedEventArgs e)
        {
            switch (_guidanceActionId)
            {
                case "open-services":
                    Launch("services.msc");
                    break;
                case "open-task-manager":
                    Launch("taskmgr.exe");
                    break;
                case "open-windows-security":
                    Launch("windowsdefender:");
                    break;
                case "open-firewall":
                    Launch("windowsdefender://network");
                    break;
                case "open-windows-update":
                    Launch("ms-settings:windowsupdate");
                    break;
                case "open-storage":
                    Launch("ms-settings:storagesense");
                    break;
                case "check-again":
                    await UpdateDashboardAsync();
                    break;
            }
        }

        private async void VerifyGuidanceButton_Click(object sender, RoutedEventArgs e)
        {
            VerifyGuidanceButton.IsEnabled = false;
            VerificationResultBorder.Visibility = Visibility.Visible;
            VerificationResultTitleText.Text = "Checking current status...";
            VerificationResultMessageText.Text = "Sentinel AI is verifying current conditions and refreshing the available evidence.";

            try
            {
                MonitoringEngine.VerificationResult result =
                    await _engine.VerifyCurrentGuidanceAsync();

                VerificationResultTitleText.Text = result.Title;
                VerificationResultMessageText.Text = result.Message;
                await UpdateDashboardAsync();
            }
            catch
            {
                VerificationResultTitleText.Text = "Verification could not complete";
                VerificationResultMessageText.Text = "Sentinel AI could not verify the current status. No system change was made.";
            }
            finally
            {
                VerifyGuidanceButton.IsEnabled = true;
            }
        }

        private static void Launch(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Guidance remains visible if Windows cannot open the requested tool.
            }
        }
    }
}