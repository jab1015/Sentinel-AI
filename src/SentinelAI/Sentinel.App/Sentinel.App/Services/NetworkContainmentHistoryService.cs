/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Windows.Storage;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Persists only the last exact endpoint for which Sentinel verified its
    /// deterministic outbound firewall block. The firewall service remains the
    /// authority for whether the rule actually exists and matches before removal.
    /// </summary>
    public sealed class NetworkContainmentHistoryService
    {
        private const string LastContainedEndpointKey = "NetworkContainment.LastVerifiedEndpoint";

        public string GetLastContainedEndpoint()
        {
            try
            {
                return ApplicationData.Current.LocalSettings.Values[LastContainedEndpointKey] as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void RecordContainedEndpoint(string remoteEndpoint)
        {
            if (string.IsNullOrWhiteSpace(remoteEndpoint)) return;

            try
            {
                ApplicationData.Current.LocalSettings.Values[LastContainedEndpointKey] = remoteEndpoint.Trim();
            }
            catch
            {
                // Persistence is convenience state only. Firewall verification,
                // not this setting, authorizes later removal.
            }
        }

        public void ClearContainedEndpoint(string remoteEndpoint)
        {
            if (string.IsNullOrWhiteSpace(remoteEndpoint)) return;

            try
            {
                string? current = ApplicationData.Current.LocalSettings.Values[LastContainedEndpointKey] as string;
                if (string.Equals(current, remoteEndpoint.Trim(), StringComparison.OrdinalIgnoreCase))
                    ApplicationData.Current.LocalSettings.Values.Remove(LastContainedEndpointKey);
            }
            catch
            {
                // A stale hint cannot authorize a firewall mutation; removal
                // always re-verifies the exact deterministic Sentinel rule.
            }
        }
    }
}
