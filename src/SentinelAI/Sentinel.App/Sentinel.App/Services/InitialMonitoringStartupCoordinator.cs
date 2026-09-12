using System;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal static class InitialMonitoringStartupCoordinator
{
    internal static async Task RunAsync(
        Func<Task> initialRefresh,
        Action startPeriodicMonitoring,
        Action<Exception>? reportInitialFailure = null)
    {
        ArgumentNullException.ThrowIfNull(initialRefresh);
        ArgumentNullException.ThrowIfNull(startPeriodicMonitoring);

        try
        {
            await initialRefresh().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            reportInitialFailure?.Invoke(ex);
        }
        finally
        {
            // A failed first snapshot must never permanently disable monitoring for
            // the rest of the application session. Later timer ticks get another
            // chance to collect evidence.
            startPeriodicMonitoring();
        }
    }
}
