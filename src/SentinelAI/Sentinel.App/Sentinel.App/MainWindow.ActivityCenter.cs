using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Sentinel.App.Services;
using System;
using System.Linq;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private readonly MaintenanceReportService _maintenanceReportService = new();
        private readonly FriendlyValueActivityService _friendlyValueActivityService = new();
        private DispatcherTimer? _activityCenterTimer;
        private long _activityVisibilityCallbackToken;
        private DateTime? _optimizationStatusUpdatedUtc;
        private string _optimizationStatusSummary = "Sentinel is checking whether this computer needs safe optimization.";

        private void ActivityCenter_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshActivityCenter();
            if (_activityVisibilityCallbackToken == 0)
            {
                _activityVisibilityCallbackToken = InvestigationHistoryBorder.RegisterPropertyChangedCallback(
                    UIElement.VisibilityProperty,
                    (_, _) =>
                    {
                        if (InvestigationHistoryBorder.Visibility != Visibility.Visible)
                            InvestigationHistoryBorder.Visibility = Visibility.Visible;
                    });
            }
            if (_activityCenterTimer is not null) return;
            _activityCenterTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _activityCenterTimer.Tick += ActivityCenterTimer_Tick;
            _activityCenterTimer.Start();
        }

        private void ActivityCenterTimer_Tick(object? sender, object e) =>
            RefreshActivityCenter();

        private void UpdateMaintenanceReport() => RefreshActivityCenter();

        private void UpdateOptimizationStatus(AutomaticOptimizationResult result)
        {
            if (result.Execution is not null && result.Execution.Attempted)
            {
                _optimizationStatusSummary = result.Execution.Succeeded
                    ? $"Optimization completed and verified. {result.Summary}"
                    : $"Sentinel checked optimization and safely stopped because a verified improvement could not be completed. {result.Summary}";
            }
            else if (!result.Baseline.IsEstablished)
            {
                _optimizationStatusSummary = $"Sentinel is learning this computer before making optimization changes ({result.Baseline.SampleCount}/12 checks complete).";
            }
            else if (!result.Decision.OptimizationWarranted)
            {
                _optimizationStatusSummary = "Optimization check complete. No verified performance optimization is needed right now; performance is within this computer's established baseline.";
            }
            else
            {
                _optimizationStatusSummary = $"Optimization check complete. {result.Summary}";
            }
            _optimizationStatusUpdatedUtc = DateTime.UtcNow;
            RefreshActivityCenter();
        }

        private void RefreshActivityCenter()
        {
            MaintenanceReport report = _maintenanceReportService.BuildReport();
            MaintenanceReportItem? latest = report.RecentItems.OrderByDescending(item => item.TimestampUtc).FirstOrDefault();

            if (latest is not null)
            {
                FriendlyValueSummaryService.FriendlyValueSummary? friendly = _friendlyValueActivityService.CreateFor(latest);
                HistoryOutcomeIconText.Text = latest.NeedsAttention ? "!" : "✓";
                HistoryTitleText.Text = friendly?.Title ?? GetUserVisibleTitle(latest);
                HistorySummaryText.Text = friendly?.Message ?? latest.Summary;
                HistoryOutcomeText.Text = $"{latest.Outcome} • {latest.TimestampUtc.ToLocalTime():MMM d, yyyy h:mm tt}";
            }
            else
            {
                HistoryOutcomeIconText.Text = "✓";
                HistoryTitleText.Text = "No recent Sentinel changes";
                HistorySummaryText.Text = "Sentinel has not needed to make a verified system change recently.";
                HistoryOutcomeText.Text = "Monitoring continues automatically";
            }

            OptimizationStatusText.Text = _optimizationStatusSummary;
            OptimizationStatusTimeText.Text = _optimizationStatusUpdatedUtc.HasValue
                ? $"Optimization check • {_optimizationStatusUpdatedUtc.Value.ToLocalTime():MMM d, yyyy h:mm tt}"
                : "Optimization check in progress";
            InvestigationHistoryBorder.Visibility = Visibility.Visible;
        }

        private static string GetUserVisibleTitle(MaintenanceReportItem item)
        {
            if (item.NeedsAttention) return "Sentinel needs your attention";
            string summary = item.Summary ?? string.Empty;
            if (item.Category.Equals("Investigation", StringComparison.OrdinalIgnoreCase)) return "Sentinel investigated an issue";
            if (item.Category.Equals("Optimization", StringComparison.OrdinalIgnoreCase))
            {
                if (summary.Contains("defrag", StringComparison.OrdinalIgnoreCase) || summary.Contains("retrim", StringComparison.OrdinalIgnoreCase) || summary.Contains("drive", StringComparison.OrdinalIgnoreCase)) return "Sentinel optimized your system drive";
                return "Sentinel optimized your computer";
            }
            if (summary.Contains("permanently deleted", StringComparison.OrdinalIgnoreCase)) return "Quarantined file permanently deleted";
            if (summary.Contains("restored the approved file", StringComparison.OrdinalIgnoreCase) || summary.Contains("restored", StringComparison.OrdinalIgnoreCase) && item.Category.Equals("Protection", StringComparison.OrdinalIgnoreCase)) return "Quarantined file restored";
            if (summary.Contains("quarantined", StringComparison.OrdinalIgnoreCase) && item.Category.Equals("Protection", StringComparison.OrdinalIgnoreCase)) return "Suspicious file quarantined";
            if (item.Outcome.Equals("Safely restored", StringComparison.OrdinalIgnoreCase)) return "Sentinel protected your computer";
            return "Sentinel fixed an issue";
        }
    }
}
