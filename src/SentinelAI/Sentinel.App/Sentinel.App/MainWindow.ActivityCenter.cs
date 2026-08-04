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
                InvestigationHistoryBorder.Visibility = Visibility.Collapsed;
                return;
            }

            MaintenanceReportItem latest = report.RecentItems
                .OrderByDescending(item => item.TimestampUtc)
                .First();

            HistoryOutcomeIconText.Text = latest.NeedsAttention ? "!" : "✓";
            HistoryTitleText.Text = latest.NeedsAttention
                ? "Sentinel needs your attention"
                : latest.Outcome.Equals("Safely restored", StringComparison.OrdinalIgnoreCase)
                    ? "Sentinel protected your computer"
                    : "Sentinel fixed an issue";

            HistorySummaryText.Text = latest.Summary;
            HistoryOutcomeText.Text = $"{latest.Outcome} • {latest.TimestampUtc.ToLocalTime():MMM d, yyyy h:mm tt}";
            InvestigationHistoryBorder.Visibility = Visibility.Visible;
        }
    }
}
