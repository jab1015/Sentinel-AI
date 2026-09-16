using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Microsoft Store subscription boundary for Sentinel AI cloud services.
    /// Local license state is useful for UI/early gating, but the cloud gateway is
    /// authoritative for paid cloud entitlements.
    /// </summary>
    public sealed class StoreSubscriptionService
    {
        public const string MonthlyOfferToken = "sentinel-ai-monthly";
        public const string AnnualOfferToken = "sentinel-ai-annual";

        private readonly object _storeContextGate = new();
        private StoreContext? _storeContext;

        public async Task<SubscriptionState> GetStateAsync()
        {
#if DEBUG || SENTINEL_LOCAL_DEV
            return new SubscriptionState(true, SubscriptionPlan.Development, "Local development build", null, null,
                "Cloud AI is enabled for this local development build. Microsoft Store subscription licensing remains enforced by the production gateway.");
#else
            if (!MicrosoftStoreRuntimePolicy.IsStoreSignedPackage(out string storePolicyDiagnostic))
            {
                return SubscriptionState.Unavailable(
                    "Free local monitoring is active. Microsoft Store subscription licensing is unavailable in this sideloaded/test package.",
                    storePolicyDiagnostic);
            }

            if (!TryGetStoreContext(out StoreContext? storeContext, out string contextDiagnostic) || storeContext is null)
            {
                return SubscriptionState.Unavailable(
                    "Sentinel could not initialize Microsoft Store licensing. Premium features will remain off; free local monitoring will continue.",
                    contextDiagnostic);
            }

            try
            {
                StoreAppLicense license = await storeContext.GetAppLicenseAsync();
                IReadOnlyList<StoreProduct> products = await GetSubscriptionProductsAsync(storeContext);

                StoreProduct? monthly = products.FirstOrDefault(p =>
                    string.Equals(p.InAppOfferToken, MonthlyOfferToken, StringComparison.OrdinalIgnoreCase));
                StoreProduct? annual = products.FirstOrDefault(p =>
                    string.Equals(p.InAppOfferToken, AnnualOfferToken, StringComparison.OrdinalIgnoreCase));

                SubscriptionPlan activePlan = SubscriptionPlan.None;
                DateTimeOffset? expiration = null;

                if (annual is not null && HasActiveAddOnLicense(license, annual.StoreId, out DateTimeOffset annualExpiration))
                {
                    activePlan = SubscriptionPlan.Annual;
                    expiration = annualExpiration;
                }
                else if (monthly is not null && HasActiveAddOnLicense(license, monthly.StoreId, out DateTimeOffset monthlyExpiration))
                {
                    activePlan = SubscriptionPlan.Monthly;
                    expiration = monthlyExpiration;
                }

                bool active = activePlan != SubscriptionPlan.None;
                string summary = active
                    ? $"Sentinel AI {activePlan.ToString().ToLowerInvariant()} subscription is active" +
                      (expiration.HasValue ? $" through {expiration.Value.LocalDateTime:d}." : ".")
                    : "Free local monitoring is active. A Sentinel AI subscription is required for premium cloud investigations and paid security/repair features.";

                return new SubscriptionState(
                    active,
                    activePlan,
                    active ? activePlan + " subscription" : "No active subscription",
                    monthly,
                    annual,
                    summary,
                    expiration);
            }
            catch (Exception ex)
            {
                return SubscriptionState.Unavailable("Sentinel could not verify the Microsoft Store subscription right now. Premium features will remain off until licensing can be verified; free local monitoring will continue.", ex.Message);
            }
#endif
        }

        /// <summary>
        /// Converts a gateway-provided, narrowly scoped Microsoft Entra collections ticket
        /// into a Microsoft Store Collections ID for the Store account currently associated
        /// with this installed package. The returned ID is sent only to the Sentinel gateway,
        /// where entitlement is verified server-side.
        /// </summary>
        public async Task<StoreCollectionsIdentityResult> CreateCollectionsIdentityAsync(
            string serviceTicket,
            string publisherUserId)
        {
            if (!MicrosoftStoreRuntimePolicy.IsStoreSignedPackage(out string storePolicyDiagnostic))
                return StoreCollectionsIdentityResult.Unavailable("Microsoft Store entitlement verification requires the retail Store package.", storePolicyDiagnostic);
            if (string.IsNullOrWhiteSpace(serviceTicket) || serviceTicket.Length > 16_384)
                return StoreCollectionsIdentityResult.Unavailable("The gateway did not provide a valid Microsoft Store collections ticket.");
            if (string.IsNullOrWhiteSpace(publisherUserId) || publisherUserId.Length > 128)
                return StoreCollectionsIdentityResult.Unavailable("The gateway did not provide a valid publisher session identifier.");
            if (!TryGetStoreContext(out StoreContext? storeContext, out string contextDiagnostic) || storeContext is null)
                return StoreCollectionsIdentityResult.Unavailable("Microsoft Store entitlement verification is unavailable right now.", contextDiagnostic);

            try
            {
                string collectionsId = await storeContext.GetCustomerCollectionsIdAsync(serviceTicket, publisherUserId);
                if (string.IsNullOrWhiteSpace(collectionsId))
                    return StoreCollectionsIdentityResult.Unavailable("Microsoft Store did not return a collections identifier.");

                return new StoreCollectionsIdentityResult(true, collectionsId, string.Empty);
            }
            catch (Exception ex)
            {
                return StoreCollectionsIdentityResult.Unavailable(
                    "Microsoft Store could not establish the server-side entitlement identity.",
                    ex.Message);
            }
        }

        public async Task<SubscriptionPurchaseResult> PurchaseAsync(SubscriptionPlan plan)
        {
            if (plan is not SubscriptionPlan.Monthly and not SubscriptionPlan.Annual)
                return new(false, "Choose a monthly or annual subscription.", StorePurchaseStatus.NotPurchased);

            if (!MicrosoftStoreRuntimePolicy.IsStoreSignedPackage(out _))
                return new(false, "Subscription purchases are available only in the installed Microsoft Store package.", StorePurchaseStatus.NotPurchased);
            if (!TryGetStoreContext(out StoreContext? storeContext, out string contextDiagnostic) || storeContext is null)
                return new(false, "Sentinel could not initialize Microsoft Store purchasing. " + contextDiagnostic, StorePurchaseStatus.ServerError);

            try
            {
                IReadOnlyList<StoreProduct> products = await GetSubscriptionProductsAsync(storeContext);
                string offerToken = plan == SubscriptionPlan.Monthly ? MonthlyOfferToken : AnnualOfferToken;
                StoreProduct? product = products.FirstOrDefault(p =>
                    string.Equals(p.InAppOfferToken, offerToken, StringComparison.OrdinalIgnoreCase));

                if (product is null)
                    return new(false, "This subscription is not available from Microsoft Store yet.", StorePurchaseStatus.NotPurchased);

                StorePurchaseResult result = await product.RequestPurchaseAsync();
                bool success = result.Status == StorePurchaseStatus.Succeeded || result.Status == StorePurchaseStatus.AlreadyPurchased;
                string message = result.Status switch
                {
                    StorePurchaseStatus.Succeeded => "Subscription activated. Sentinel can now verify premium entitlement with the secure cloud gateway.",
                    StorePurchaseStatus.AlreadyPurchased => "This subscription is already active on your Microsoft account.",
                    StorePurchaseStatus.NotPurchased => "The subscription purchase was not completed.",
                    StorePurchaseStatus.NetworkError => "Microsoft Store could not complete the purchase because of a network error.",
                    StorePurchaseStatus.ServerError => "Microsoft Store could not complete the purchase right now.",
                    _ => "Microsoft Store did not complete the subscription purchase."
                };
                return new(success, message, result.Status);
            }
            catch (Exception ex)
            {
                return new(false, "Sentinel could not open the Microsoft Store purchase flow. " + ex.Message, StorePurchaseStatus.ServerError);
            }
        }

        private bool TryGetStoreContext(out StoreContext? storeContext, out string diagnostic)
        {
            lock (_storeContextGate)
            {
                if (_storeContext is not null)
                {
                    storeContext = _storeContext;
                    diagnostic = string.Empty;
                    return true;
                }

                try
                {
                    _storeContext = StoreContext.GetDefault();
                    storeContext = _storeContext;
                    diagnostic = storeContext is null ? "Microsoft Store returned no StoreContext." : string.Empty;
                    return storeContext is not null;
                }
                catch (Exception ex)
                {
                    storeContext = null;
                    diagnostic = $"Microsoft Store context initialization failed ({ex.GetType().Name}): {ex.Message}";
                    return false;
                }
            }
        }

        private static async Task<IReadOnlyList<StoreProduct>> GetSubscriptionProductsAsync(StoreContext storeContext)
        {
            StoreProductQueryResult query = await storeContext.GetAssociatedStoreProductsAsync(new[] { "Durable" });
            if (query.ExtendedError is not null)
                throw query.ExtendedError;
            return query.Products.Values.ToArray();
        }

        private static bool HasActiveAddOnLicense(StoreAppLicense appLicense, string storeId, out DateTimeOffset expiration)
        {
            expiration = default;
            if (!appLicense.AddOnLicenses.TryGetValue(storeId, out StoreLicense? addOn) || !addOn.IsActive)
                return false;
            expiration = addOn.ExpirationDate;
            return true;
        }
    }

    public enum SubscriptionPlan
    {
        None,
        Monthly,
        Annual,
        Development
    }

    public sealed record SubscriptionState(
        bool IsActive,
        SubscriptionPlan Plan,
        string DisplayName,
        StoreProduct? MonthlyProduct,
        StoreProduct? AnnualProduct,
        string Summary,
        DateTimeOffset? ExpirationDate = null,
        string Diagnostic = "")
    {
        public static SubscriptionState Unavailable(string summary, string diagnostic = "") =>
            new(false, SubscriptionPlan.None, "Subscription unavailable", null, null, summary, null, diagnostic);
    }

    public sealed record StoreCollectionsIdentityResult(bool Succeeded, string CollectionsId, string Message, string Diagnostic = "")
    {
        public static StoreCollectionsIdentityResult Unavailable(string message, string diagnostic = "") =>
            new(false, string.Empty, message, diagnostic);
    }

    public sealed record SubscriptionPurchaseResult(bool Succeeded, string Message, StorePurchaseStatus Status);
}
