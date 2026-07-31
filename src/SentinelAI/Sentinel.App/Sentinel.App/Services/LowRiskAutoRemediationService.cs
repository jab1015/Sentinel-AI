/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Handles only remediation that is safe to perform without changing user
    /// applications, files, firewall policy, security settings, services, or
    /// protected Windows components.
    ///
    /// At this stage the only automatic recovery is a fresh verification pass.
    /// It allows Sentinel to clear transient conditions automatically while all
    /// state-changing remediation remains approval-gated by RemediationPolicy.
    /// </summary>
    public sealed class LowRiskAutoRemediationService
    {
        public async Task<AutoRemediationResult> TryRecoverAsync(
            SystemSnapshot snapshot,
            Func<Task> refreshAsync,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(refreshAsync);

            cancellationToken.ThrowIfCancellationRequested();

            if (!snapshot.InvestigationRequiresAttention)
            {
                return AutoRemediationResult.NotRequired(
                    "No automatic recovery is required for the current healthy state.");
            }

            // Never silently execute state-changing actions. These are deliberately
            // excluded from low-risk automatic recovery.
            if (snapshot.RemediationAvailable && snapshot.RemediationRequiresUserApproval)
            {
                return AutoRemediationResult.NotPerformed(
                    "A supported remediation is available, but it requires user approval before Sentinel may change the system.");
            }

            // A fresh evidence pass is safe and can automatically resolve stale or
            // transient findings without touching system state.
            await refreshAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return new AutoRemediationResult(
                Performed: true,
                ChangedSystemState: false,
                RequiresUserApproval: false,
                Action: "refresh-and-reverify",
                Message: "Sentinel automatically refreshed and re-verified the current evidence without changing the system.");
        }

        public sealed record AutoRemediationResult(
            bool Performed,
            bool ChangedSystemState,
            bool RequiresUserApproval,
            string Action,
            string Message)
        {
            public static AutoRemediationResult NotRequired(string message) =>
                new(false, false, false, "none", message);

            public static AutoRemediationResult NotPerformed(string message) =>
                new(false, false, true, "approval-required", message);
        }
    }
}
