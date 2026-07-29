using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Sentinel.App.Services;
using System;

namespace Sentinel.App
{
    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherTimer _timer = new();
        private readonly MonitoringEngine _engine = new();
        private bool _isRefreshing;

        public MainWindow()
        {
            InitializeComponent();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
            _ = UpdateDashboardAsync();
        }

        private async void Timer_Tick(object? sender, object e)
        {
            await UpdateDashboardAsync();
        }

        private async System.Threading.Tasks.Task UpdateDashboardAsync()
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

                RiskScoreText.Text = snapshot.RiskScore.ToString();
                RiskLevelText.Text = $"{snapshot.RiskLevel} Risk";
                RiskSummaryText.Text = snapshot.RiskSummary;
                RecommendationText.Text = snapshot.Recommendation;
                LastUpdatedText.Text = $"Last Updated: {snapshot.Timestamp:hh:mm:ss tt}";
            }
            finally
            {
                _isRefreshing = false;
            }
        }
    }
}
