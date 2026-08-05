/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Bridges the adaptive cadence policy to the live monitoring loop without
    /// depending on WinUI. The UI timer applies RecommendedInterval after each
    /// completed Discovery pass.
    /// </summary>
    public sealed class LiveAdaptiveDiscoveryScheduler
    {
        private readonly AdaptiveDiscoveryCadenceService _cadenceService;

        public LiveAdaptiveDiscoveryScheduler(AdaptiveDiscoveryCadenceService? cadenceService = null)
        {
            _cadenceService = cadenceService ?? new AdaptiveDiscoveryCadenceService();
        }

        public LiveScheduleDecision Evaluate(
            SystemSnapshot snapshot,
            TimeSpan currentInterval,
            bool persistentNotificationSuppressed = false,
            bool applicationIsIdle = false,
            bool onBattery = false)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            AdaptiveDiscoveryCadenceService.AdaptiveDiscoveryDecision cadence =
                _cadenceService.Evaluate(
                    snapshot,
                    applicationIsIdle,
                    onBattery,
                    persistentNotificationSuppressed);

            TimeSpan interval = cadence.NextCheckInterval <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(1)
                : cadence.NextCheckInterval;

            return new LiveScheduleDecision(
                cadence.Priority,
                interval,
                interval != currentInterval,
                cadence.AllowDeepVerification,
                cadence.Reason,
                MonitoringEnabled: true);
        }

        public sealed record LiveScheduleDecision(
            AdaptiveDiscoveryCadenceService.DiscoveryPriority Priority,
            TimeSpan RecommendedInterval,
            bool IntervalChanged,
            bool AllowDeepVerification,
            string Reason,
            bool MonitoringEnabled);
    }
}
