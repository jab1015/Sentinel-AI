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
        return Task.CompletedTask;
    },
    () => successTimerStarts++,
    _ => successFailures++);
Require(successRefreshCalls == 1, "Successful initial refresh was not called exactly once.");
Require(successTimerStarts == 1, "Periodic monitoring was not started exactly once after successful initial refresh.");
Require(successFailures == 0, "Successful initial refresh was reported as failed.");

int failedRefreshCalls = 0;
int failedTimerStarts = 0;
int reportedFailures = 0;
await InitialMonitoringStartupCoordinator.RunAsync(
    () =>
    {
        failedRefreshCalls++;
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

Console.WriteLine("Successful first refresh starts periodic monitoring: PASS");
Console.WriteLine("Failed first refresh still starts periodic monitoring: PASS");
Console.WriteLine("Canceled first refresh still starts periodic monitoring: PASS");
Console.WriteLine("RESULT: PASS");
