using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Microsoft Store subscription boundary for Sentinel AI cloud services.
    /// The Partner Center Product IDs (offer tokens) are stable identifiers chosen by us;
    /// Store IDs are discovered at runtime and never hard-coded.
    /// </summary>
    public sealed class StoreSubscriptionService
    {
        public const string MonthlyOfferToken = "sentinel-ai-monthly";
        public const string AnnualOfferToken = "sentinel-ai-annual";

        private readonly StoreContext _storeContext;

        public StoreSubscriptionService()
        {
            _storeContext = StoreContext.GetDefault();
        }

        public async Task<SubscriptionState> GetStateAsync()
        {
#if DEBUG || SENTINEL_LOCAL_DEV
            return new SubscriptionState(true, SubscriptionPlan.Development, "Local development build", null, null,
                "Cloud AI is enabled for this local development build. Microsoft Store subscription licensing remains enforced in Release packages.");
#else
            if (!HasPackageIdentity())
                return SubscriptionState.Unavailable("Microsoft Store licensing is available only in an installed Sentinel package.");

            try
            {
                StoreAppLicense license = await _storeContext.GetAppLicenseAsync();
                IReadOnlyList<StoreProduct> products = await GetSubscriptionProductsAsync();

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
                    : "A Sentinel AI subscription is required for cloud AI investigations. Local monitoring remains available without one.";

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
                return SubscriptionState.Unavailable("Sentinel could not verify the Microsoft Store subscription right now. Cloud AI will remain off until licensing can be verified.", ex.Message);
            }
#endif
        }

        public async Task<SubscriptionPurchaseResult> PurchaseAsync(SubscriptionPlan plan)
        {
            if (plan is not SubscriptionPlan.Monthly and not SubscriptionPlan.Annual)
                return new(false, "Choose a monthly or annual subscription.", StorePurchaseStatus.NotPurchased);

            if (!HasPackageIdentity())
                return new(false, "Subscription purchases are available only in the installed Microsoft Store package.", StorePurchaseStatus.NotPurchased);

            try
            {
                IReadOnlyList<StoreProduct> products = await GetSubscriptionProductsAsync();
                string offerToken = plan == SubscriptionPlan.Monthly ? MonthlyOfferToken : AnnualOfferToken;
                StoreProduct? product = products.FirstOrDefault(p =>
                    string.Equals(p.InAppOfferToken, offerToken, StringComparison.OrdinalIgnoreCase));

                if (product is null)
                    return new(false, "This subscription is not available from Microsoft Store yet.", StorePurchaseStatus.NotPurchased);

                StorePurchaseResult result = await product.RequestPurchaseAsync();
                bool success = result.Status == StorePurchaseStatus.Succeeded || result.Status == StorePurchaseStatus.AlreadyPurchased;
                string message = result.Status switch
                {
                    StorePurchaseStatus.Succeeded => "Subscription activated. Sentinel can now use cloud AI when local evidence is not enough.",
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

        private async Task<IReadOnlyList<StoreProduct>> GetSubscriptionProductsAsync()
        {
            StoreProductQueryResult query = await _storeContext.GetAssociatedStoreProductsAsync(new[] { "Durable" });
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

        private static bool HasPackageIdentity()
        {
            try { return !string.IsNullOrWhiteSpace(Package.Current.Id.FamilyName); }
            catch (InvalidOperationException) { return false; }
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

    public sealed record SubscriptionPurchaseResult(bool Succeeded, string Message, StorePurchaseStatus Status);
}
