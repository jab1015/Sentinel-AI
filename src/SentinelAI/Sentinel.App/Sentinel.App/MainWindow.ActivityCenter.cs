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
        private DispatcherTimer? _activityCenterTimer;

        private void ActivityCenter_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshActivityCenter();

            if (_activityCenterTimer is not null)
                return;

            _activityCenterTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _activityCenterTimer.Tick += (_, _) => RefreshActivityCenter();
            _activityCenterTimer.Start();
        }

        private void RefreshActivityCenter()
        {
            MaintenanceReport report = _maintenanceReportService.BuildReport();

            if (report.RecentItems.Count == 0)
            {
                HistoryOutcomeIconText.Text = "✓";
                HistoryTitleText.Text = "No action required";
                HistorySummaryText.Text = "Sentinel has not needed to perform any maintenance recently. Your computer is being monitored normally.";
                HistoryOutcomeText.Text = "Monitoring continues automatically";
                InvestigationHistoryBorder.Visibility = Visibility.Visible;
                return;
            }

            MaintenanceReportItem latest = report.RecentItems
                .OrderByDescending(item => item.TimestampUtc)
                .First();

            HistoryOutcomeIconText.Text = latest.NeedsAttention ? "!" : "✓";
            HistoryTitleText.Text = GetUserVisibleTitle(latest);
            HistorySummaryText.Text = latest.Summary;
            HistoryOutcomeText.Text = $"{latest.Outcome} • {latest.TimestampUtc.ToLocalTime():MMM d, yyyy h:mm tt}";
            InvestigationHistoryBorder.Visibility = Visibility.Visible;
        }

        private static string GetUserVisibleTitle(MaintenanceReportItem item)
        {
            if (item.NeedsAttention)
                return "Sentinel needs your attention";

            string summary = item.Summary ?? string.Empty;

            if (summary.Contains("permanently deleted", StringComparison.OrdinalIgnoreCase))
                return "Quarantined file permanently deleted";

            if (summary.Contains("restored the approved file", StringComparison.OrdinalIgnoreCase) ||
                summary.Contains("restored", StringComparison.OrdinalIgnoreCase) &&
                item.Category.Equals("Protection", StringComparison.OrdinalIgnoreCase))
                return "Quarantined file restored";

            if (summary.Contains("quarantined", StringComparison.OrdinalIgnoreCase) &&
                item.Category.Equals("Protection", StringComparison.OrdinalIgnoreCase))
                return "Suspicious file quarantined";

            if (item.Outcome.Equals("Safely restored", StringComparison.OrdinalIgnoreCase))
                return "Sentinel protected your computer";

            return "Sentinel fixed an issue";
        }
    }
}
