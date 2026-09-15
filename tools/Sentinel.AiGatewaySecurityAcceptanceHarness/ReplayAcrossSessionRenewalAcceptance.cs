using System.Net;
using System.Runtime.CompilerServices;

internal static class ReplayAcrossSessionRenewalAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        Environment.SetEnvironmentVariable(
            "SENTINEL_GATEWAY_SESSION_SIGNING_KEY",
            "acceptance-harness-signing-key-32-bytes-minimum");

        var security = new GatewaySecurity(new RenewalFakeHttpClientFactory());
        string requestId = Guid.NewGuid().ToString();

        string? firstToken = security.IssueSession("Basic", "renewal-test");
        if (string.IsNullOrWhiteSpace(firstToken))
            throw new InvalidOperationException("Replay renewal acceptance failed: first session was not issued.");

        SessionValidationResult first = security.ValidateSession(
            "Bearer " + firstToken,
            requestId,
            "Basic");
        if (!first.Available || !first.Authorized)
            throw new InvalidOperationException("Replay renewal acceptance failed: first request was not authorized.");

        string? renewedToken = security.IssueSession("Basic", "renewal-test");
        if (string.IsNullOrWhiteSpace(renewedToken))
            throw new InvalidOperationException("Replay renewal acceptance failed: renewed session was not issued.");

        SessionValidationResult renewedReplay = security.ValidateSession(
            "Bearer " + renewedToken,
            requestId,
            "Basic");
        if (renewedReplay.Authorized ||
            !renewedReplay.Reason.Contains("already", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Replay renewal acceptance failed: the same subject replayed one request ID after session renewal.");
        }

        string? otherSubjectToken = security.IssueSession("Basic", "renewal-test-other-subject");
        if (string.IsNullOrWhiteSpace(otherSubjectToken))
            throw new InvalidOperationException("Replay renewal acceptance failed: other-subject session was not issued.");

        SessionValidationResult isolated = security.ValidateSession(
            "Bearer " + otherSubjectToken,
            requestId,
            "Basic");
        if (!isolated.Available || !isolated.Authorized)
        {
            throw new InvalidOperationException(
                "Replay renewal acceptance failed: replay identity was not isolated between authenticated subjects.");
        }
    }

    private sealed class RenewalFakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new RenewalRejectNetworkHandler()) { BaseAddress = new Uri("https://invalid.local/") };
    }

    private sealed class RenewalRejectNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
