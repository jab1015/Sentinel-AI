/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Produces low-noise diagnostic events for adaptive Discovery cadence changes.
    /// It records only meaningful scheduling transitions so Activity Center and
    /// diagnostic logs can explain why Sentinel changed monitoring frequency.
    /// </summary>
    public sealed class AdaptiveDiscoveryDiagnosticService
    {
        private DiagnosticState? _lastState;

        public AdaptiveDiscoveryDiagnosticResult Evaluate(
            LiveAdaptiveDiscoveryScheduler.LiveScheduleDecision decision,
            bool persistentNotificationSuppressed)
        {
            ArgumentNullException.ThrowIfNull(decision);

            DiagnosticState current = new(
                decision.Priority,
                decision.RecommendedInterval,
                decision.AllowDeepVerification,
                persistentNotificationSuppressed);

            if (_lastState is not null && _lastState == current)
            {
                return new AdaptiveDiscoveryDiagnosticResult(false, string.Empty, string.Empty, string.Empty);
            }

            DiagnosticState? previous = _lastState;
            _lastState = current;

            string title = previous is null
                ? "Adaptive Discovery initialized"
                : "Adaptive Discovery cadence changed";

            string summary = BuildSummary(current, previous);
            string technical =
                $"Priority: {current.Priority}; Interval: {current.Interval.TotalSeconds:0.#} seconds; " +
                $"Deep verification: {current.AllowDeepVerification}; Persistent notification suppressed: {current.PersistentNotificationSuppressed}; " +
                $"Monitoring enabled: {decision.MonitoringEnabled}; Reason: {decision.Reason}";

            return new AdaptiveDiscoveryDiagnosticResult(true, title, summary, technical);
        }

        private static string BuildSummary(DiagnosticState current, DiagnosticState? previous)
        {
            string interval = FormatInterval(current.Interval);

            if (current.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.Critical)
                return $"Sentinel increased Discovery frequency to {interval} because verified critical evidence requires faster rechecks.";

            if (current.Priority == AdaptiveDiscoveryCadenceService.DiscoveryPriority.High)
                return $"Sentinel is checking every {interval} while a verified condition requires attention.";

            if (current.PersistentNotificationSuppressed)
                return $"Sentinel reduced repeated checks to {interval} for an unchanged known condition while continuing background monitoring.";

            if (current.AllowDeepVerification)
                return $"Sentinel is using a {interval} cadence and may perform deeper background verification while system conditions allow.";

            if (previous is not null && current.Interval > previous.Interval)
                return $"Sentinel reduced background polling to every {interval} because no urgent condition currently requires faster checks.";

            return $"Sentinel is using an adaptive Discovery interval of {interval}.";
        }

        private static string FormatInterval(TimeSpan interval)
        {
            if (interval.TotalMinutes >= 1 && interval.TotalSeconds % 60 == 0)
                return interval.TotalMinutes == 1 ? "1 minute" : $"{interval.TotalMinutes:0} minutes";

            return interval.TotalSeconds == 1 ? "1 second" : $"{interval.TotalSeconds:0.#} seconds";
        }

        private sealed record DiagnosticState(
            AdaptiveDiscoveryCadenceService.DiscoveryPriority Priority,
            TimeSpan Interval,
            bool AllowDeepVerification,
            bool PersistentNotificationSuppressed);

        public sealed record AdaptiveDiscoveryDiagnosticResult(
            bool ShouldRecord,
            string Title,
            string Summary,
            string TechnicalDetail);
    }
}
