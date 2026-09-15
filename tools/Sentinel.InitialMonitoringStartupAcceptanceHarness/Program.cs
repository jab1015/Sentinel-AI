using Sentinel.App.Services;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Console.WriteLine("=== Sentinel AI Initial Monitoring Startup Acceptance ===");

int successRefreshCalls = 0;
int successTimerStarts = 0;
int successFailures = 0;
await InitialMonitoringStartupCoordinator.RunAsync(
    () =>
    {
        successRefreshCalls++;
        Require(InitialMonitoringStartupCoordinator.IsGatheringInformation,
            "Startup gathering state was not active while the initial refresh was running.");
        return Task.CompletedTask;
    },
    () => successTimerStarts++,
    _ => successFailures++);
Require(successRefreshCalls == 1, "Successful initial refresh was not called exactly once.");
Require(successTimerStarts == 1, "Periodic monitoring was not started exactly once after successful initial refresh.");
Require(successFailures == 0, "Successful initial refresh was reported as failed.");
Require(InitialMonitoringStartupCoordinator.IsGatheringInformation,
    "Startup gathering state ended immediately instead of allowing later collectors to complete.");

int failedRefreshCalls = 0;
int failedTimerStarts = 0;
int reportedFailures = 0;
await InitialMonitoringStartupCoordinator.RunAsync(
    () =>
    {
        failedRefreshCalls++;
        Require(InitialMonitoringStartupCoordinator.IsGatheringInformation,
            "Startup gathering state was not active during a failing first refresh.");
        throw new InvalidOperationException("deterministic first-refresh failure");
    },
    () => failedTimerStarts++,
    ex =>
    {
        Require(ex is InvalidOperationException, "Unexpected exception type reached the failure reporter.");
        reportedFailures++;
    });
Require(failedRefreshCalls == 1, "Failing initial refresh was not called exactly once.");
Require(reportedFailures == 1, "Initial refresh failure was not reported exactly once.");
Require(failedTimerStarts == 1, "Periodic monitoring did not start after the initial refresh failed.");
Require(InitialMonitoringStartupCoordinator.IsGatheringInformation,
    "A failed first refresh incorrectly disabled the bounded gathering state.");

int canceledTimerStarts = 0;
int canceledFailures = 0;
await InitialMonitoringStartupCoordinator.RunAsync(
    () => Task.FromCanceled(new CancellationToken(canceled: true)),
    () => canceledTimerStarts++,
    ex =>
    {
        Require(ex is OperationCanceledException, "Cancellation was not surfaced as cancellation evidence.");
        canceledFailures++;
    });
Require(canceledFailures == 1, "Initial refresh cancellation was not reported.");
Require(canceledTimerStarts == 1, "Periodic monitoring did not start after initial refresh cancellation.");
Require(InitialMonitoringStartupCoordinator.IsGatheringInformation,
    "Initial refresh cancellation incorrectly disabled the bounded gathering state.");
Require(InitialMonitoringStartupCoordinator.GatheringGracePeriod > TimeSpan.Zero &&
        InitialMonitoringStartupCoordinator.GatheringGracePeriod <= TimeSpan.FromMinutes(2),
    "Startup gathering grace period is missing or unreasonably long.");

Console.WriteLine("Successful first refresh starts periodic monitoring: PASS");
Console.WriteLine("Failed first refresh still starts periodic monitoring: PASS");
Console.WriteLine("Canceled first refresh still starts periodic monitoring: PASS");
Console.WriteLine("Bounded startup gathering state survives the first collection pass: PASS");
Console.WriteLine("RESULT: PASS");
