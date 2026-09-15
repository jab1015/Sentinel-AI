using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

internal sealed class PremiumPrivacyEntitlementClient : IDisposable
{
    internal const string EncryptScope = "privacy.encrypt";
    internal const string VaultScope = "privacy.vault";
    internal const string SecureDeleteScope = "privacy.secure-delete";
    internal const string DiscoveryScope = "privacy.discovery";

    private const string ProductionGatewayRoot = "https://sentinel-ai-gateway-49908265995.us-central1.run.app/";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly StoreSubscriptionService _storeSubscription;
    private readonly Uri? _gatewayRoot;
    private bool _disposed;

    internal PremiumPrivacyEntitlementClient(
        HttpClient? httpClient = null,
        StoreSubscriptionService? storeSubscription = null,
        string? gatewayRootOverride = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = RequestTimeout,
            MaxResponseContentBufferSize = 256 * 1024
        };
        _storeSubscription = storeSubscription ?? new StoreSubscriptionService();

        string root = ProductionGatewayRoot;
#if DEBUG
        string? configured = gatewayRootOverride ?? Environment.GetEnvironmentVariable("SENTINEL_AI_GATEWAY_URL");
        if (!string.IsNullOrWhiteSpace(configured) && Uri.TryCreate(configured.Trim(), UriKind.Absolute, out Uri? configuredUri))
            root = configuredUri.GetLeftPart(UriPartial.Authority) + "/";
#endif
        if (Uri.TryCreate(root, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
            _gatewayRoot = uri;
    }

    internal async Task<PremiumPrivacyAuthorizationResult> AuthorizeOneShotAsync(
        string scope,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PremiumPrivacyEntitlementClient));
        if (!IsSupportedScope(scope))
            return PremiumPrivacyAuthorizationResult.Denied("UnsupportedScope", "The requested Premium Privacy capability is unsupported.");

#if SENTINEL_LOCAL_DEV
        // VM-only test entitlement. SENTINEL_LOCAL_DEV is defined exclusively by the
        // LocalDev build configuration. Release/Store builds compile the authoritative
        // Microsoft Store + Sentinel gateway path below and cannot activate this branch
        // through an environment variable, local preference, or runtime toggle.
        return PremiumPrivacyAuthorizationResult.LocalVmTestAllowed(scope);
#else
        if (_gatewayRoot is null)
            return PremiumPrivacyAuthorizationResult.Unavailable("GatewayUnavailable", "Premium Privacy entitlement verification is not configured.");

        try
        {
            Uri ticketUri = new(_gatewayRoot, "v1/store/collections-ticket");
            using HttpResponseMessage ticketResponse = await _httpClient.GetAsync(ticketUri, cancellationToken).ConfigureAwait(false);
            if (!ticketResponse.IsSuccessStatusCode)
                return PremiumPrivacyAuthorizationResult.Unavailable("StoreBootstrapUnavailable", "Sentinel could not start authoritative Microsoft Store entitlement verification.");

            StoreCollectionsTicketResponse? ticket = await ticketResponse.Content.ReadFromJsonAsync<StoreCollectionsTicketResponse>(
                JsonOptions, cancellationToken).ConfigureAwait(false);
            if (ticket is null || string.IsNullOrWhiteSpace(ticket.ServiceTicket) || string.IsNullOrWhiteSpace(ticket.PublisherUserId))
                return PremiumPrivacyAuthorizationResult.Unavailable("StoreBootstrapInvalid", "The gateway returned an invalid Store entitlement bootstrap response.");

            StoreCollectionsIdentityResult collections = await _storeSubscription.CreateCollectionsIdentityAsync(
                ticket.ServiceTicket,
                ticket.PublisherUserId).ConfigureAwait(false);
            if (!collections.Succeeded)
                return PremiumPrivacyAuthorizationResult.Denied("StoreIdentityUnavailable",
                    string.IsNullOrWhiteSpace(collections.Message)
                        ? "Microsoft Store could not verify the installed Sentinel account."
                        : collections.Message);

            Uri sessionUri = new(_gatewayRoot, "v1/session/store");
            using HttpResponseMessage sessionResponse = await _httpClient.PostAsJsonAsync(
                sessionUri,
                new StoreSessionRequest(1, collections.CollectionsId),
                cancellationToken).ConfigureAwait(false);
            if (sessionResponse.StatusCode == HttpStatusCode.Forbidden)
                return PremiumPrivacyAuthorizationResult.Denied("SubscriptionInactive", "An active Sentinel subscription is required for Premium Privacy creation and cleanup features.");
            if (!sessionResponse.IsSuccessStatusCode)
                return PremiumPrivacyAuthorizationResult.Unavailable("StoreSessionUnavailable", "The secure gateway could not establish a paid Store session.");

            GatewaySessionResponse? session = await sessionResponse.Content.ReadFromJsonAsync<GatewaySessionResponse>(
                JsonOptions, cancellationToken).ConfigureAwait(false);
            if (session is null || string.IsNullOrWhiteSpace(session.AccessToken) || session.AccessToken.Length > 16_384 ||
                !string.Equals(session.Tier, "Advanced", StringComparison.OrdinalIgnoreCase) || session.ExpiresInSeconds <= 0)
                return PremiumPrivacyAuthorizationResult.Unavailable("StoreSessionInvalid", "The secure gateway returned an invalid paid session.");

            Uri capabilityUri = new(_gatewayRoot, "v1/privacy/capability");
            using HttpRequestMessage issueMessage = new(HttpMethod.Post, capabilityUri)
            {
                Content = JsonContent.Create(new PrivacyCapabilityRequest(
                    1,
                    Guid.NewGuid().ToString(),
                    collections.CollectionsId,
                    scope))
            };
            issueMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            using HttpResponseMessage issueResponse = await _httpClient.SendAsync(issueMessage, cancellationToken).ConfigureAwait(false);
            if (issueResponse.StatusCode == HttpStatusCode.Forbidden)
                return PremiumPrivacyAuthorizationResult.Denied("SubscriptionInactive", "Microsoft Store did not verify an active Premium Privacy entitlement.");
            if (issueResponse.StatusCode == HttpStatusCode.Unauthorized)
                return PremiumPrivacyAuthorizationResult.Denied("SessionRejected", "The paid gateway session was rejected.");
            if (!issueResponse.IsSuccessStatusCode)
                return PremiumPrivacyAuthorizationResult.Unavailable("CapabilityUnavailable", "The secure gateway could not issue a Premium Privacy capability.");

            PrivacyCapabilityResponse? capability = await issueResponse.Content.ReadFromJsonAsync<PrivacyCapabilityResponse>(
                JsonOptions, cancellationToken).ConfigureAwait(false);
            if (capability is null || string.IsNullOrWhiteSpace(capability.Capability) || capability.Capability.Length > 16_384 ||
                !string.Equals(capability.Scope, scope, StringComparison.Ordinal) || capability.ExpiresInSeconds is <= 0 or > 60)
                return PremiumPrivacyAuthorizationResult.Unavailable("CapabilityInvalid", "The secure gateway returned an invalid Premium Privacy capability.");

            // Consume immediately before the caller performs the local premium operation.
            // The backend re-queries Store during this validation, catching revocation/expiry
            // that occurred after the paid session or capability was issued.
            Uri validationUri = new(_gatewayRoot, "v1/privacy/capability/validate");
            using HttpResponseMessage validationResponse = await _httpClient.PostAsJsonAsync(
                validationUri,
                new PrivacyCapabilityValidationRequest(
                    1,
                    collections.CollectionsId,
                    capability.Capability,
                    scope),
                cancellationToken).ConfigureAwait(false);
            if (validationResponse.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                return PremiumPrivacyAuthorizationResult.Denied("CapabilityRejected", "Premium Privacy entitlement changed, expired, or could not be revalidated for this operation.");
            if (!validationResponse.IsSuccessStatusCode)
                return PremiumPrivacyAuthorizationResult.Unavailable("CapabilityValidationUnavailable", "Premium Privacy entitlement could not be revalidated. No premium mutation was authorized.");

            PrivacyCapabilityValidationResponse? validation = await validationResponse.Content.ReadFromJsonAsync<PrivacyCapabilityValidationResponse>(
                JsonOptions, cancellationToken).ConfigureAwait(false);
            if (validation is null || !validation.Authorized || !string.Equals(validation.Scope, scope, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(validation.TokenId))
                return PremiumPrivacyAuthorizationResult.Denied("CapabilityRejected", "The scoped Premium Privacy capability was not accepted.");

            return PremiumPrivacyAuthorizationResult.Allowed(scope, validation.TokenId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PremiumPrivacyAuthorizationResult.Unavailable("Timeout", "Premium Privacy entitlement verification timed out. No premium operation was authorized.");
        }
        catch (HttpRequestException)
        {
            return PremiumPrivacyAuthorizationResult.Unavailable("NetworkUnavailable", "Premium Privacy entitlement verification is offline. Existing encrypted data remains recoverable, but new premium creation/cleanup is unavailable.");
        }
        catch (JsonException)
        {
            return PremiumPrivacyAuthorizationResult.Unavailable("InvalidGatewayResponse", "The secure gateway returned invalid entitlement data. No premium operation was authorized.");
        }
        catch (InvalidOperationException)
        {
            return PremiumPrivacyAuthorizationResult.Unavailable("StoreUnavailable", "Microsoft Store entitlement verification is unavailable. No premium operation was authorized.");
        }
#endif
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }

    private static bool IsSupportedScope(string? scope) => scope is
        EncryptScope or VaultScope or SecureDeleteScope or DiscoveryScope;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record StoreCollectionsTicketResponse(string ServiceTicket, string PublisherUserId);
    private sealed record StoreSessionRequest(int SchemaVersion, string CollectionsId);
    private sealed record GatewaySessionResponse(string AccessToken, string? Tier, int ExpiresInSeconds);
    private sealed record PrivacyCapabilityRequest(int SchemaVersion, string RequestId, string CollectionsId, string Scope);
    private sealed record PrivacyCapabilityResponse(string Capability, string Scope, int ExpiresInSeconds);
    private sealed record PrivacyCapabilityValidationRequest(int SchemaVersion, string CollectionsId, string Capability, string Scope);
    private sealed record PrivacyCapabilityValidationResponse(bool Authorized, string Scope, string TokenId);
}

internal sealed record PremiumPrivacyAuthorizationResult(
    bool Succeeded,
    bool ServiceAvailable,
    string Scope,
    string TokenId,
    string Code,
    string Message)
{
    internal static PremiumPrivacyAuthorizationResult Allowed(string scope, string tokenId) =>
        new(true, true, scope, tokenId, "Authorized", "Active Microsoft Store entitlement and one-time feature capability were verified by the Sentinel gateway.");

    internal static PremiumPrivacyAuthorizationResult LocalVmTestAllowed(string scope) =>
        new(true, true, scope, "LOCAL-VM-TEST-" + Guid.NewGuid().ToString("N"),
            "LocalVmTestAuthorized",
            "LOCALDEV VM TEST BUILD: Premium Privacy subscription verification is bypassed for isolated Windows validation. Release and Microsoft Store builds still require authoritative Store and Sentinel gateway verification.");

    internal static PremiumPrivacyAuthorizationResult Denied(string code, string message) =>
        new(false, true, string.Empty, string.Empty, code, message);

    internal static PremiumPrivacyAuthorizationResult Unavailable(string code, string message) =>
        new(false, false, string.Empty, string.Empty, code, message);
}
