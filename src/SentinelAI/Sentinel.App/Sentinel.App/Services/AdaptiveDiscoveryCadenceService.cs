/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Determines how aggressively Sentinel should schedule the next Discovery pass.
    /// This policy never disables monitoring. It only adjusts cadence so urgent
    /// conditions are revisited quickly while quiet systems avoid unnecessary work.
    /// </summary>
    public sealed class AdaptiveDiscoveryCadenceService
    {
        public AdaptiveDiscoveryDecision Evaluate(SystemSnapshot snapshot, bool applicationIsIdle = false, bool onBattery = false)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            if (IsCritical(snapshot))
            {
                return new AdaptiveDiscoveryDecision(
                    DiscoveryPriority.Critical,
                    TimeSpan.FromSeconds(2),
                    true,
                    "Critical verified evidence requires immediate continued investigation.");
            }

            if (IsHigh(snapshot))
            {
                return new AdaptiveDiscoveryDecision(
                    DiscoveryPriority.High,
                    TimeSpan.FromSeconds(5),
                    true,
                    "A verified condition requires attention, so Sentinel should recheck it quickly.");
            }

            if (IsMedium(snapshot))
            {
                TimeSpan interval = onBattery ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(15);
                return new AdaptiveDiscoveryDecision(
                    DiscoveryPriority.Medium,
                    interval,
                    false,
                    onBattery
                        ? "Moderate-priority evidence is queued with a reduced battery cadence."
                        : "Moderate-priority evidence should be revisited on the normal investigation queue.");
            }

            if (applicationIsIdle && !onBattery)
            {
                return new AdaptiveDiscoveryDecision(
                    DiscoveryPriority.Low,
                    TimeSpan.FromSeconds(15),
                    true,
                    "The computer is quiet, so Sentinel may use the opportunity for deeper background verification.");
            }

            return new AdaptiveDiscoveryDecision(
                DiscoveryPriority.Low,
                onBattery ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(30),
                false,
                onBattery
                    ? "No verified condition requires attention; Sentinel reduces background cadence while on battery."
                    : "No verified condition requires attention; Sentinel continues normal low-impact monitoring.");
        }

        private static bool IsCritical(SystemSnapshot snapshot)
        {
            if (!snapshot.DefenderEnabled || !snapshot.FirewallEnabled)
                return true;

            if (snapshot.InvestigationRequiresAttention &&
                (Contains(snapshot.GuidanceSeverity, "critical") ||
                 Contains(snapshot.RiskLevel, "critical") ||
                 snapshot.InvestigationShouldEscalate))
                return true;

            return false;
        }

        private static bool IsHigh(SystemSnapshot snapshot)
        {
            if (snapshot.InvestigationRequiresAttention)
                return true;

            if (snapshot.AutonomousProtectionRequiresUserApproval || snapshot.FlaggedConnectionCount > 0)
                return true;

            return false;
        }

        private static bool IsMedium(SystemSnapshot snapshot)
        {
            if (string.Equals(snapshot.MemoryPressureLevel, "High", StringComparison.OrdinalIgnoreCase))
                return true;

            return snapshot.FlaggedProcessCount > 0 ||
                   snapshot.FlaggedServiceCount > 0 ||
                   snapshot.FlaggedStartupEntryCount > 0 ||
                   snapshot.FlaggedScheduledTaskCount > 0;
        }

        private static bool Contains(string? value, string term) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(term, StringComparison.OrdinalIgnoreCase);

        public enum DiscoveryPriority
        {
            Critical,
            High,
            Medium,
            Low
        }

        public sealed record AdaptiveDiscoveryDecision(
            DiscoveryPriority Priority,
            TimeSpan NextCheckInterval,
            bool AllowDeepVerification,
            string Reason);
    }
}
