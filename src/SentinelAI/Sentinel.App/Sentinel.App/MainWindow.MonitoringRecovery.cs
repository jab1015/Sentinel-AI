using System;
using Sentinel.App.Services;

namespace Sentinel.App
{
    public sealed partial class MainWindow
    {
        private readonly DiagnosticLogService _monitoringDiagnosticLog = new();

        /// <summary>
        /// Installs an exception-contained timer handler and starts the scheduler
        /// independently of the initial dashboard refresh. A transient first-refresh
        /// failure therefore cannot permanently disable later monitoring cycles.
        /// </summary>
        public void EnsureMonitoringSchedulerRunning()
        {
            _timer.Tick -= Timer_Tick;
            _timer.Tick -= SafeMonitoringTimerTick;
            _timer.Tick += SafeMonitoringTimerTick;
            _timer.Start();
        }

        private async void SafeMonitoringTimerTick(object? sender, object e)
        {
            try
            {
                await UpdateDashboardAsync();
            }
            catch (Exception ex)
            {
                _ = _monitoringDiagnosticLog.ErrorAsync(
                    "MonitoringCycleFailure",
                    "A monitoring cycle failed. The scheduler remains active and will retry on the next cycle.",
                    ex);
            }
        }
    }
}
