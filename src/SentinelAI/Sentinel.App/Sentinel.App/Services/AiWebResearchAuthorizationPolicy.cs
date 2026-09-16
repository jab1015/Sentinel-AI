/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Desktop-side defense in depth for provider web research. The server is the
    /// authoritative entitlement/tool boundary, but the Windows client independently
    /// refuses web-derived response metadata unless this request was both Advanced and
    /// explicitly the external-investigation reasoning pass.
    /// </summary>
    public static class AiWebResearchAuthorizationPolicy
    {
        public static bool IsAuthorized(bool advanced, string? purpose) =>
            advanced &&
            string.Equals(
                purpose?.Trim(),
                "external-investigation",
                StringComparison.OrdinalIgnoreCase);
    }
}
