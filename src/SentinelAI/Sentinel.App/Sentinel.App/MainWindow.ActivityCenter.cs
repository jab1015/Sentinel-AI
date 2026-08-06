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

            if (_activityCenterTimer is not null)
                return;

            _activityCenterTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _activityCenterTimer.Tick += (_, _) => RefreshActivityCenter();
            _activityCenterTimer.Start();
        }

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
                _optimizationStatusSummary = "Optimization check complete. No verified optimization is needed right now; performance is within this computer's established baseline.";
            }
            else
            {
                _optimizationStatusSummary = $"Optimization check complete. {result.Summary}";
            }

            RefreshActivityCenter();
        }

        private void RefreshActivityCenter()
        {
            MaintenanceReport report = _maintenanceReportService.BuildReport();

            if (report.RecentItems.Count == 0)
            {
                HistoryOutcomeIconText.Text = "✓";
                HistoryTitleText.Text = "Sentinel is monitoring and optimizing automatically";
                HistorySummaryText.Text = _optimizationStatusSummary;
                HistoryOutcomeText.Text = "Monitoring and optimization checks continue automatically";
                InvestigationHistoryBorder.Visibility = Visibility.Visible;
                return;
            }

            MaintenanceReportItem latest = report.RecentItems.OrderByDescending(item => item.TimestampUtc).First();
            FriendlyValueSummaryService.FriendlyValueSummary? friendly = _friendlyValueActivityService.CreateFor(latest);

            HistoryOutcomeIconText.Text = latest.NeedsAttention ? "!" : "✓";
            HistoryTitleText.Text = friendly?.Title ?? GetUserVisibleTitle(latest);
            HistorySummaryText.Text = friendly?.Message ?? latest.Summary;
            HistoryOutcomeText.Text = $"{latest.Outcome} • {latest.TimestampUtc.ToLocalTime():MMM d, yyyy h:mm tt}";
            InvestigationHistoryBorder.Visibility = Visibility.Visible;
        }

        private static string GetUserVisibleTitle(MaintenanceReportItem item)
        {
            if (item.NeedsAttention) return "Sentinel needs your attention";
            string summary = item.Summary ?? string.Empty;
            if (item.Category.Equals("Investigation", StringComparison.OrdinalIgnoreCase)) return "Sentinel investigated an issue";
            if (item.Category.Equals("Optimization", StringComparison.OrdinalIgnoreCase)) return "Sentinel optimized your computer";
            if (summary.Contains("permanently deleted", StringComparison.OrdinalIgnoreCase)) return "Quarantined file permanently deleted";
            if (summary.Contains("restored the approved file", StringComparison.OrdinalIgnoreCase) || summary.Contains("restored", StringComparison.OrdinalIgnoreCase) && item.Category.Equals("Protection", StringComparison.OrdinalIgnoreCase)) return "Quarantined file restored";
            if (summary.Contains("quarantined", StringComparison.OrdinalIgnoreCase) && item.Category.Equals("Protection", StringComparison.OrdinalIgnoreCase)) return "Suspicious file quarantined";
            if (item.Outcome.Equals("Safely restored", StringComparison.OrdinalIgnoreCase)) return "Sentinel protected your computer";
            return "Sentinel fixed an issue";
        }
    }
}
