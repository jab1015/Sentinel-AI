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
            await _engine.RefreshAsync();

            var snapshot = _engine.CurrentSnapshot;

            CpuText.Text =
                $"CPU Usage: {snapshot.CpuUsagePercent:0.0}%";

            MemoryText.Text =
                $"Memory: {snapshot.MemoryUsedGB:0.00} GB / {snapshot.MemoryTotalGB:0.00} GB ({snapshot.MemoryUsagePercent:0.0}%)";

            double diskUsedGB = Math.Max(snapshot.DiskTotalGB - snapshot.DiskFreeGB, 0);
            DiskText.Text = snapshot.DiskTotalGB > 0
                ? $"Disk: {diskUsedGB:0.00} GB / {snapshot.DiskTotalGB:0.00} GB ({snapshot.DiskUsagePercent:0.0}%)"
                : "Disk: Unavailable";

            NetworkText.Text =
                $"Network: ↓ {snapshot.DownloadMbps:0.00} Mbps   ↑ {snapshot.UploadMbps:0.00} Mbps";

            LastUpdatedText.Text =
                $"Last Updated: {snapshot.Timestamp:hh:mm:ss tt}";
        }
    }
}
