using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Sentinel.App.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private readonly MaintenanceReportService _maintenanceReportService = new();
        private readonly FriendlyValueActivityService _friendlyValueActivityService = new();
        private DispatcherTimer? _activityCenterTimer;
        private long _activityVisibilityCallbackToken;
        private bool _optimizationStatusRefreshRunning;
        private DateTime? _optimizationStatusUpdatedUtc;
        private string _optimizationStatusSummary = "Sentinel is checking whether this computer needs safe optimization.";

        private async void ActivityCenter_Loaded(object sender, RoutedEventArgs e)
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

            await RefreshOptimizationStatusAsync();
            if (_activityCenterTimer is not null) return;
            _activityCenterTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _activityCenterTimer.Tick += ActivityCenterTimer_Tick;
            _activityCenterTimer.Start();
        }

        private async void ActivityCenterTimer_Tick(object? sender, object e)
        {
            RefreshActivityCenter();
            await RefreshOptimizationStatusAsync();
        }

        private async Task RefreshOptimizationStatusAsync()
        {
            if (_optimizationStatusRefreshRunning) return;
            _optimizationStatusRefreshRunning = true;
            try
            {
                AutomaticOptimizationResult result = await _automaticOptimizationCoordinator.EvaluateAndRunAsync(_engine.CurrentSnapshot);
                UpdateOptimizationStatus(result);
            }
            catch
            {
                _optimizationStatusSummary = "Sentinel could not verify optimization status during this check. Monitoring continues and Sentinel will try again automatically.";
                _optimizationStatusUpdatedUtc = DateTime.UtcNow;
                RefreshActivityCenter();
            }
            finally { _optimizationStatusRefreshRunning = false; }
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

            // An actual recorded Sentinel action always outranks a passive optimization check.
            // This prevents a fresh "no optimization needed" assessment from hiding a verified
            // defrag/retrim/repair that Sentinel itself performed and recorded.
            if (latest is not null)
            {
                FriendlyValueSummaryService.FriendlyValueSummary? friendly = _friendlyValueActivityService.CreateFor(latest);
                HistoryOutcomeIconText.Text = latest.NeedsAttention ? "!" : "✓";
                HistoryTitleText.Text = friendly?.Title ?? GetUserVisibleTitle(latest);
                HistorySummaryText.Text = friendly?.Message ?? latest.Summary;
                HistoryOutcomeText.Text = $"{latest.Outcome} • {latest.TimestampUtc.ToLocalTime():MMM d, yyyy h:mm tt}";
                InvestigationHistoryBorder.Visibility = Visibility.Visible;
                return;
            }

            HistoryOutcomeIconText.Text = "✓";
            HistoryTitleText.Text = "Sentinel checked computer optimization";
            HistorySummaryText.Text = _optimizationStatusSummary;
            HistoryOutcomeText.Text = _optimizationStatusUpdatedUtc.HasValue
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
                if (item.Action.Contains("defrag", StringComparison.OrdinalIgnoreCase)) return "Sentinel optimized your system drive";
                if (item.Action.Contains("retrim", StringComparison.OrdinalIgnoreCase)) return "Sentinel optimized your system drive";
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
