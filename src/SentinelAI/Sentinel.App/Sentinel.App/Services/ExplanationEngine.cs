/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts investigation outcomes into short, non-technical explanations.
    /// The explanation layer never changes the underlying investigation result.
    /// </summary>
    public sealed class ExplanationEngine
    {
        public ExplanationResult Explain(SystemSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            if (!snapshot.InvestigationRequiresAttention)
            {
                if (snapshot.InvestigationState.Equals("Investigating", StringComparison.OrdinalIgnoreCase))
                {
                    return new ExplanationResult(
                        "Sentinel is checking something in the background.",
                        "No action is required right now. Sentinel is waiting for another signal before deciding whether the activity matters.",
                        "Continue using your computer normally.");
                }

                return new ExplanationResult(
                    "Your computer looks healthy.",
                    "Sentinel did not find enough evidence to identify a security problem.",
                    "No action is required.");
            }

            string reason = snapshot.InvestigationReasonCode ?? string.Empty;

            if (reason.Contains("security-protection", StringComparison.OrdinalIgnoreCase))
            {
                return new ExplanationResult(
                    "A Windows security protection is turned off.",
                    "Sentinel confirmed that Defender or the Windows Firewall is not enabled, which reduces the protection of this computer.",
                    "Review the Windows security setting before continuing with remediation.");
            }

            if (reason.Contains("correlated", StringComparison.OrdinalIgnoreCase))
            {
                return new ExplanationResult(
                    "Several security signals point to the same activity.",
                    "Sentinel found independent evidence that appears to involve the same process or behavior, making the finding more significant than a single warning.",
                    "Review the investigation details before taking action.");
            }

            if (reason.Contains("service-failure", StringComparison.OrdinalIgnoreCase))
            {
                return new ExplanationResult(
                    "A Windows service stopped unexpectedly.",
                    "Sentinel confirmed a service failure that may affect a Windows feature or application.",
                    "Use Sentinel's verification step before changing the service configuration.");
            }

            return new ExplanationResult(
                "Sentinel found a condition that needs attention.",
                string.IsNullOrWhiteSpace(snapshot.InvestigationSummary)
                    ? "The available evidence is strong enough to require review."
                    : snapshot.InvestigationSummary,
                "Review the investigation details before making changes.");
        }

        public sealed record ExplanationResult(
            string WhatHappened,
            string WhyItMatters,
            string RecommendedAction);
    }
}
