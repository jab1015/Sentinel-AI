/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Deterministic fail-closed mapping from approved remediation action names
    /// to dedicated execution coordinators.
    /// </summary>
    public static class ApprovedRemediationRoutingPolicy
    {
        public static ApprovedRemediationRoute Resolve(string? action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return ApprovedRemediationRoute.Unsupported;

            return action.Trim().ToLowerInvariant() switch
            {
                "restart-service" => ApprovedRemediationRoute.ServiceRestart,
                "block-outbound-endpoint" => ApprovedRemediationRoute.FirewallContainment,
                "contain-process" => ApprovedRemediationRoute.ProcessContainment,
                "quarantine-file" or
                "restore-quarantined-file" or
                "delete-quarantined-file" => ApprovedRemediationRoute.Quarantine,
                _ => ApprovedRemediationRoute.Unsupported
            };
        }
    }

    public enum ApprovedRemediationRoute
    {
        Unsupported = 0,
        ServiceRestart,
        FirewallContainment,
        ProcessContainment,
        Quarantine
    }
}
