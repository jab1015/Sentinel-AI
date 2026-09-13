using System;

namespace Sentinel.App.Services
{
    internal static class SecurityHealthClassificationPolicy
    {
        internal static string ClassifyDefender(
            bool? serviceEnabled,
            bool? antivirusEnabled,
            bool? realTimeProtectionEnabled,
            string? runningMode,
            int? signatureAgeDays)
        {
            if (!serviceEnabled.HasValue || !antivirusEnabled.HasValue || !realTimeProtectionEnabled.HasValue)
                return "Unavailable";

            if (!string.IsNullOrWhiteSpace(runningMode) &&
                runningMode.Contains("Passive", StringComparison.OrdinalIgnoreCase))
                return "Passive";

            if (!serviceEnabled.Value || !antivirusEnabled.Value || !realTimeProtectionEnabled.Value)
                return "Disabled or inactive";

            if (signatureAgeDays.HasValue && signatureAgeDays.Value > 3)
                return "Enabled (signatures stale)";

            return "Enabled";
        }

        internal static string ClassifyFirewall(
            string? serviceStatus,
            int expectedProfileCount,
            int observedProfileCount,
            int enabledProfileCount)
        {
            if (!string.Equals(serviceStatus, "Running", StringComparison.OrdinalIgnoreCase))
                return "Disabled or inactive";

            if (expectedProfileCount < 3 ||
                observedProfileCount != expectedProfileCount ||
                enabledProfileCount < 0 ||
                enabledProfileCount > observedProfileCount)
                return "Unavailable";

            if (enabledProfileCount == observedProfileCount)
                return "Enabled";

            if (enabledProfileCount == 0)
                return "Disabled";

            return $"Partial ({enabledProfileCount} of {observedProfileCount} active profiles enabled)";
        }
    }
}
