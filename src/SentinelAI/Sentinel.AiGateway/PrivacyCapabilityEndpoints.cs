using System.Security.Cryptography;
using System.Text;

internal static class PrivacyCapabilityEndpoints
{
    internal static void MapPrivacyCapabilityEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/privacy/capability", async (
            HttpContext context,
            PrivacyCapabilityRequest request,
            GatewaySecurity gatewaySecurity,
            PrivacyCapabilitySecurity capabilitySecurity,
            CancellationToken cancellationToken) =>
        {
            if (request.SchemaVersion != 1 || string.IsNullOrWhiteSpace(request.RequestId) ||
                string.IsNullOrWhiteSpace(request.CollectionsId) || string.IsNullOrWhiteSpace(request.Scope))
                return Results.BadRequest(new { error = "A request ID, Store identity, and privacy scope are required." });

            SessionValidationResult session = gatewaySecurity.ValidateSession(
                context.Request.Headers.Authorization.ToString(),
                request.RequestId,
                "Advanced");
            if (!session.Available)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (!session.Authorized)
                return Results.Json(new { error = session.Reason }, statusCode: StatusCodes.Status401Unauthorized);

            string expectedSubject = SubjectForCollectionsId(request.CollectionsId);
            if (!string.Equals(session.Subject, expectedSubject, StringComparison.Ordinal))
                return Results.Json(new { error = "The Store identity does not match the authenticated paid session." }, statusCode: StatusCodes.Status401Unauthorized);

            // Re-query Store at capability issuance instead of trusting the cached UI/local license.
            StoreEntitlementResult entitlement = await gatewaySecurity.VerifyPaidEntitlementAsync(
                request.CollectionsId,
                cancellationToken).ConfigureAwait(false);
            if (!entitlement.Available)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (!entitlement.IsActive)
                return Results.Json(new { error = "No active paid Sentinel entitlement was verified." }, statusCode: StatusCodes.Status403Forbidden);

            PrivacyCapabilityIssueResult issued = capabilitySecurity.Issue(expectedSubject, request.Scope);
            if (!issued.Available)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (!issued.Authorized)
                return Results.Json(new { error = issued.Reason }, statusCode: StatusCodes.Status403Forbidden);

            return Results.Ok(new PrivacyCapabilityResponse(
                issued.Token,
                issued.Scope,
                issued.ExpiresInSeconds));
        });

        app.MapPost("/v1/privacy/capability/validate", async (
            PrivacyCapabilityValidationRequest request,
            GatewaySecurity gatewaySecurity,
            PrivacyCapabilitySecurity capabilitySecurity,
            CancellationToken cancellationToken) =>
        {
            if (request.SchemaVersion != 1 || string.IsNullOrWhiteSpace(request.Capability) ||
                string.IsNullOrWhiteSpace(request.CollectionsId) || string.IsNullOrWhiteSpace(request.Scope))
                return Results.BadRequest(new { error = "A privacy capability, Store identity, and scope are required." });

            string expectedSubject = SubjectForCollectionsId(request.CollectionsId);
            PrivacyCapabilityValidationResult validation = capabilitySecurity.ValidateAndConsume(
                request.Capability,
                request.Scope,
                expectedSubject);
            if (!validation.Available)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (!validation.Authorized)
                return Results.Json(new { authorized = false, error = validation.Reason }, statusCode: StatusCodes.Status401Unauthorized);

            // Destructive/local premium execution performs a fresh authoritative Store check.
            // A revoked/expired entitlement therefore cannot ride a previously-issued capability.
            StoreEntitlementResult entitlement = await gatewaySecurity.VerifyPaidEntitlementAsync(
                request.CollectionsId,
                cancellationToken).ConfigureAwait(false);
            if (!entitlement.Available)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (!entitlement.IsActive)
                return Results.Json(new { authorized = false, error = "The paid entitlement is no longer active." }, statusCode: StatusCodes.Status403Forbidden);

            return Results.Ok(new PrivacyCapabilityValidationResponse(
                Authorized: true,
                Scope: validation.Scope,
                TokenId: validation.TokenId));
        });
    }

    private static string SubjectForCollectionsId(string collectionsId)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(collectionsId));
        return "store:" + Convert.ToHexString(digest.AsSpan(0, 12));
    }
}

internal sealed record PrivacyCapabilityRequest(
    int SchemaVersion,
    string RequestId,
    string CollectionsId,
    string Scope);

internal sealed record PrivacyCapabilityResponse(
    string Capability,
    string Scope,
    int ExpiresInSeconds);

internal sealed record PrivacyCapabilityValidationRequest(
    int SchemaVersion,
    string CollectionsId,
    string Capability,
    string Scope);

internal sealed record PrivacyCapabilityValidationResponse(
    bool Authorized,
    string Scope,
    string TokenId);
