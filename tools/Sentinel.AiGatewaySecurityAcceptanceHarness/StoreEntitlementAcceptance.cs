using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;

internal static class StoreEntitlementAcceptance
{
    [ModuleInitializer]
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        Console.WriteLine("--- StoreEntitlementAcceptance: START ---");

        string? priorTenant = Environment.GetEnvironmentVariable("SENTINEL_STORE_TENANT_ID");
        string? priorClient = Environment.GetEnvironmentVariable("SENTINEL_STORE_CLIENT_ID");
        string? priorSecret = Environment.GetEnvironmentVariable("SENTINEL_STORE_CLIENT_SECRET");
        string? priorProducts = Environment.GetEnvironmentVariable("SENTINEL_STORE_PAID_PRODUCT_IDS");
        string? priorSandbox = Environment.GetEnvironmentVariable("SENTINEL_STORE_SANDBOX");

        Environment.SetEnvironmentVariable("SENTINEL_STORE_TENANT_ID", "acceptance-tenant");
        Environment.SetEnvironmentVariable("SENTINEL_STORE_CLIENT_ID", "acceptance-client");
        Environment.SetEnvironmentVariable("SENTINEL_STORE_CLIENT_SECRET", "acceptance-secret");
        Environment.SetEnvironmentVariable("SENTINEL_STORE_PAID_PRODUCT_IDS", "9N67THV2Z1GP");
        Environment.SetEnvironmentVariable("SENTINEL_STORE_SANDBOX", "RETAIL");

        try
        {
            await VerifyStateAsync("Active", expectedAvailable: true, expectedActive: true, "Active subscriber accepted");
            await VerifyStateAsync("Expired", expectedAvailable: true, expectedActive: false, "Expired subscriber rejected");
            await VerifyStateAsync("Revoked", expectedAvailable: true, expectedActive: false, "Revoked subscriber rejected");
            await VerifyStateAsync("Inactive", expectedAvailable: true, expectedActive: false, "Inactive subscriber rejected");

            GatewaySecurity malformed = new(new StoreFixtureHttpClientFactory(StoreFixtureMode.MalformedStore));
            StoreEntitlementResult malformedResult = await malformed.VerifyPaidEntitlementAsync("collections-id", CancellationToken.None);
            Require(!malformedResult.Available && !malformedResult.IsActive,
                "Malformed Store response did not fail closed as unavailable.");

            GatewaySecurity storeUnavailable = new(new StoreFixtureHttpClientFactory(StoreFixtureMode.StoreUnavailable));
            StoreEntitlementResult unavailableResult = await storeUnavailable.VerifyPaidEntitlementAsync("collections-id", CancellationToken.None);
            Require(!unavailableResult.Available && !unavailableResult.IsActive,
                "Store service outage did not fail closed as unavailable.");

            GatewaySecurity networkUnavailable = new(new StoreFixtureHttpClientFactory(StoreFixtureMode.NetworkUnavailable));
            StoreEntitlementResult networkResult = await networkUnavailable.VerifyPaidEntitlementAsync("collections-id", CancellationToken.None);
            Require(!networkResult.Available && !networkResult.IsActive,
                "Network outage did not fail closed as unavailable.");

            GatewaySecurity wrongProduct = new(new StoreFixtureHttpClientFactory(StoreFixtureMode.ActiveWrongProduct));
            StoreEntitlementResult wrongProductResult = await wrongProduct.VerifyPaidEntitlementAsync("collections-id", CancellationToken.None);
            Require(wrongProductResult.Available && !wrongProductResult.IsActive,
                "Unrelated active Store product was accepted as Sentinel Premium.");

            StoreEntitlementResult malformedIdentity = await wrongProduct.VerifyPaidEntitlementAsync("   ", CancellationToken.None);
            Require(malformedIdentity.Available && !malformedIdentity.IsActive,
                "Malformed collections identity did not fail closed.");

            Environment.SetEnvironmentVariable("SENTINEL_STORE_CLIENT_SECRET", null);
            GatewaySecurity missingServerCredential = new(new StoreFixtureHttpClientFactory(StoreFixtureMode.Active));
            StoreEntitlementResult missingCredentialResult = await missingServerCredential.VerifyPaidEntitlementAsync("collections-id", CancellationToken.None);
            Require(!missingCredentialResult.Available && !missingCredentialResult.IsActive,
                "Missing server-side Store credential did not fail closed.");

            Console.WriteLine("Active/inactive/expired/revoked Store state handling: PASS");
            Console.WriteLine("Wrong-product and malformed identity rejection: PASS");
            Console.WriteLine("Store/network/backend configuration unavailable fail-closed: PASS");
            Console.WriteLine("--- StoreEntitlementAcceptance: COMPLETE ---");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SENTINEL_STORE_TENANT_ID", priorTenant);
            Environment.SetEnvironmentVariable("SENTINEL_STORE_CLIENT_ID", priorClient);
            Environment.SetEnvironmentVariable("SENTINEL_STORE_CLIENT_SECRET", priorSecret);
            Environment.SetEnvironmentVariable("SENTINEL_STORE_PAID_PRODUCT_IDS", priorProducts);
            Environment.SetEnvironmentVariable("SENTINEL_STORE_SANDBOX", priorSandbox);
        }
    }

    private static async Task VerifyStateAsync(string status, bool expectedAvailable, bool expectedActive, string message)
    {
        GatewaySecurity security = new(new StoreFixtureHttpClientFactory(StoreFixtureMode.State, status));
        StoreEntitlementResult result = await security.VerifyPaidEntitlementAsync("collections-id", CancellationToken.None);
        Require(result.Available == expectedAvailable && result.IsActive == expectedActive, message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal enum StoreFixtureMode
{
    State,
    Active,
    ActiveWrongProduct,
    MalformedStore,
    StoreUnavailable,
    NetworkUnavailable
}

internal sealed class StoreFixtureHttpClientFactory : IHttpClientFactory
{
    private readonly StoreFixtureMode _mode;
    private readonly string _status;

    internal StoreFixtureHttpClientFactory(StoreFixtureMode mode, string status = "Active")
    {
        _mode = mode;
        _status = status;
    }

    public HttpClient CreateClient(string name) => new(new StoreFixtureHandler(name, _mode, _status));
}

internal sealed class StoreFixtureHandler : HttpMessageHandler
{
    private readonly string _clientName;
    private readonly StoreFixtureMode _mode;
    private readonly string _status;

    internal StoreFixtureHandler(string clientName, StoreFixtureMode mode, string status)
    {
        _clientName = clientName;
        _mode = mode;
        _status = status;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_clientName == "entra")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { access_token = "acceptance-service-token" })
            });
        }

        if (_clientName != "store")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        if (_mode == StoreFixtureMode.NetworkUnavailable)
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("Injected network outage."));

        if (_mode == StoreFixtureMode.StoreUnavailable)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        if (_mode == StoreFixtureMode.MalformedStore)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not-json}")
            });
        }

        string productId = _mode == StoreFixtureMode.ActiveWrongProduct ? "UNRELATED-PRODUCT" : "9N67THV2Z1GP";
        string status = _mode == StoreFixtureMode.State ? _status : "Active";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                items = new[] { new { productId, status } }
            })
        });
    }
}
