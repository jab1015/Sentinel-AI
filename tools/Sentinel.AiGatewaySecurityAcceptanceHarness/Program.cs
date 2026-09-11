using System.Net;
using System.Net.Http;

Environment.SetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY", "acceptance-harness-signing-key-32-bytes-minimum");

var security = new GatewaySecurity(new FakeHttpClientFactory());
int failures = 0;

void Check(bool condition, string name)
{
    Console.WriteLine($"{name}: {(condition ? "PASS" : "FAIL")}");
    if (!condition) failures++;
}

string? basicToken = security.IssueSession("Basic", "acceptance-basic");
Check(!string.IsNullOrWhiteSpace(basicToken), "Basic session issued");

string basicRequest = Guid.NewGuid().ToString();
SessionValidationResult basic = security.ValidateSession("Bearer " + basicToken, basicRequest, "Basic");
Check(basic.Available && basic.Authorized && basic.Tier == "Basic", "Basic session authorizes Basic request");

SessionValidationResult basicReplay = security.ValidateSession("Bearer " + basicToken, basicRequest, "Basic");
Check(!basicReplay.Authorized && basicReplay.Reason.Contains("already", StringComparison.OrdinalIgnoreCase), "Replay is rejected");

SessionValidationResult upgrade = security.ValidateSession("Bearer " + basicToken, Guid.NewGuid().ToString(), "Advanced");
Check(!upgrade.Authorized, "Basic session cannot authorize Advanced request");

string? advancedToken = security.IssueSession("Advanced", "acceptance-paid");
Check(!string.IsNullOrWhiteSpace(advancedToken), "Advanced session issued");
SessionValidationResult advanced = security.ValidateSession("Bearer " + advancedToken, Guid.NewGuid().ToString(), "Advanced");
Check(advanced.Authorized && advanced.Tier == "Advanced", "Advanced session authorizes Advanced request");

string tampered = (advancedToken ?? string.Empty) + "x";
SessionValidationResult tamperedResult = security.ValidateSession("Bearer " + tampered, Guid.NewGuid().ToString(), "Advanced");
Check(!tamperedResult.Authorized, "Tampered session is rejected");

SessionValidationResult anonymous = security.ValidateSession(null, Guid.NewGuid().ToString(), "Basic");
Check(!anonymous.Authorized, "Anonymous analyze request is rejected");

SessionValidationResult badRequestId = security.ValidateSession("Bearer " + basicToken, "not-a-guid", "Basic");
Check(!badRequestId.Authorized, "Malformed request ID is rejected");

Environment.SetEnvironmentVariable("SENTINEL_GATEWAY_SESSION_SIGNING_KEY", "too-short");
var misconfigured = new GatewaySecurity(new FakeHttpClientFactory());
Check(misconfigured.IssueSession("Basic", "test") is null, "Weak signing key fails closed");

Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
return failures == 0 ? 0 : 1;

sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new RejectNetworkHandler()) { BaseAddress = new Uri("https://invalid.local/") };
}

sealed class RejectNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
}
