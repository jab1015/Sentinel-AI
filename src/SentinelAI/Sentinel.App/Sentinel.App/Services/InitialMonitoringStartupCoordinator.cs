using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal static class InitialMonitoringStartupCoordinator
{
    // Initial collectors do not all become authoritative on the same refresh. During
    // this bounded warm-up window Sentinel reports that it is gathering evidence
    // instead of presenting a temporary missing collector as a product failure.
    // The window expires automatically so a collector that truly stays unavailable
    // is still surfaced as degraded monitoring.
    internal static readonly TimeSpan GatheringGracePeriod = TimeSpan.FromSeconds(60);
    private static long _gatheringUntilUtcTicks;

    internal static bool IsGatheringInformation
    {
        get
        {
            long untilTicks = Volatile.Read(ref _gatheringUntilUtcTicks);
            return untilTicks > 0 && DateTime.UtcNow.Ticks <= untilTicks;
        }
    }

    internal static async Task RunAsync(
        Func<Task> initialRefresh,
        Action startPeriodicMonitoring,
        Action<Exception>? reportInitialFailure = null)
    {
        ArgumentNullException.ThrowIfNull(initialRefresh);
        ArgumentNullException.ThrowIfNull(startPeriodicMonitoring);

        BeginGatheringWindow();

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

    private static void BeginGatheringWindow()
    {
        long untilTicks = DateTime.UtcNow.Add(GatheringGracePeriod).Ticks;
        Interlocked.Exchange(ref _gatheringUntilUtcTicks, untilTicks);
    }
}
