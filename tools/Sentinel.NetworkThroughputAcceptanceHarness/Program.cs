using Sentinel.App.Services;

static void AssertClose(double actual, double expected, double tolerance, string message)
{
    if (Math.Abs(actual - expected) > tolerance)
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

const long frequency = 1_000;

var previous = new Dictionary<string, NetworkCounterSample>(StringComparer.OrdinalIgnoreCase)
{
    ["a"] = new(1_000_000, 500_000, 1_000),
    ["b"] = new(2_000_000, 1_000_000, 500)
};
var current = new Dictionary<string, NetworkCounterSample>(StringComparer.OrdinalIgnoreCase)
{
    ["a"] = new(2_000_000, 1_000_000, 2_000),
    ["b"] = new(3_500_000, 1_750_000, 2_000)
};

NetworkThroughputCalculation mixedIntervals = NetworkThroughputPolicy.Calculate(previous, current, frequency);
Assert(mixedIntervals.ContributingAdapters == 2, "Both stable adapters should contribute.");
AssertClose(mixedIntervals.DownloadMbps, 16.0, 0.0001, "Per-adapter download rates must be summed using each adapter interval.");
AssertClose(mixedIntervals.UploadMbps, 8.0, 0.0001, "Per-adapter upload rates must be summed using each adapter interval.");

var churned = new Dictionary<string, NetworkCounterSample>(StringComparer.OrdinalIgnoreCase)
{
    ["a"] = new(2_000_000, 1_000_000, 2_000),
    ["new"] = new(50_000_000, 40_000_000, 2_000)
};
NetworkThroughputCalculation churn = NetworkThroughputPolicy.Calculate(previous, churned, frequency);
Assert(churn.ContributingAdapters == 1, "A newly appearing adapter must establish a baseline rather than create a spike.");
AssertClose(churn.DownloadMbps, 8.0, 0.0001, "Only the surviving adapter should contribute after membership churn.");

var resetCurrent = new Dictionary<string, NetworkCounterSample>(StringComparer.OrdinalIgnoreCase)
{
    ["a"] = new(100, 100, 2_000)
};
NetworkThroughputCalculation reset = NetworkThroughputPolicy.Calculate(previous, resetCurrent, frequency);
Assert(reset.ContributingAdapters == 0, "Counter reset/wrap must establish a new baseline.");
AssertClose(reset.DownloadMbps, 0, 0.0001, "Counter reset must not create a throughput spike.");

var clockBackwards = new Dictionary<string, NetworkCounterSample>(StringComparer.OrdinalIgnoreCase)
{
    ["a"] = new(2_000_000, 1_000_000, 900)
};
NetworkThroughputCalculation clock = NetworkThroughputPolicy.Calculate(previous, clockBackwards, frequency);
Assert(clock.ContributingAdapters == 0, "Non-positive timestamp delta must be rejected.");

NetworkThroughputCalculation firstSample = NetworkThroughputPolicy.Calculate(
    new Dictionary<string, NetworkCounterSample>(),
    new Dictionary<string, NetworkCounterSample> { ["a"] = new(1, 1, 1) },
    frequency);
Assert(firstSample.IsConnected && firstSample.ContributingAdapters == 0, "First sample should report connectivity without inventing throughput.");

NetworkThroughputCalculation invalidFrequency = NetworkThroughputPolicy.Calculate(previous, current, 0);
Assert(invalidFrequency.ContributingAdapters == 0, "Invalid timer frequency must fail closed.");

Console.WriteLine("Network throughput acceptance harness PASS");
