/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Verifies whether Sentinel's own monitoring pipeline and the Windows protection
    /// layers it depends on are healthy enough to provide continuous protection.
    /// </summary>
    public sealed class ProtectionHealthEngine
    {
        public ProtectionHealthResult Evaluate(SystemSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            bool networkHealthy = snapshot.NetworkConnectionMonitoringAvailable &&
                                  snapshot.NetworkConnectionMonitoringStatus.Equals("Active", StringComparison.OrdinalIgnoreCase);
            bool defenderHealthy = snapshot.DefenderEnabled;
            bool firewallHealthy = snapshot.FirewallEnabled;
            bool monitoringCoverageHealthy =
                snapshot.AuthenticationMonitoringAvailable &&
                snapshot.StartupPersistenceMonitoringAvailable &&
                snapshot.ScheduledTaskMonitoringAvailable;

            int degradedComponents = 0;
            if (!networkHealthy) degradedComponents++;
            if (!defenderHealthy) degradedComponents++;
            if (!firewallHealthy) degradedComponents++;
            if (!monitoringCoverageHealthy) degradedComponents++;

            if (degradedComponents == 0)
            {
                return new ProtectionHealthResult(
                    ProtectionHealthState.Healthy,
                    true,
                    "Protection is active",
                    "Sentinel network, authentication, startup-persistence, and scheduled-task monitoring are active. Microsoft Defender and Windows Firewall are also active.",
                    "No action is required.",
                    "protection-healthy");
            }

            if (!networkHealthy)
            {
                return new ProtectionHealthResult(
                    ProtectionHealthState.Degraded,
                    false,
                    "Sentinel network protection is degraded",
                    "Sentinel cannot currently verify continuous network connection monitoring.",
                    "Keep Sentinel running. If this condition persists, restart Sentinel; if monitoring does not recover, restart Windows.",
                    "protection-network-monitor-unavailable");
            }

            if (!monitoringCoverageHealthy)
            {
                string[] unavailable =
                {
                    snapshot.AuthenticationMonitoringAvailable ? string.Empty : "authentication",
                    snapshot.StartupPersistenceMonitoringAvailable ? string.Empty : "startup persistence",
                    snapshot.ScheduledTaskMonitoringAvailable ? string.Empty : "scheduled task"
                };

                string missing = string.Join(", ", Array.FindAll(unavailable, value => !string.IsNullOrWhiteSpace(value)));
                return new ProtectionHealthResult(
                    ProtectionHealthState.Degraded,
                    false,
                    "Sentinel security monitoring coverage is degraded",
                    $"Sentinel cannot currently verify {missing} monitoring.",
                    "Keep Sentinel running while it retries. If coverage does not recover, review Activity Center and restart Sentinel before relying on a healthy security status.",
                    "protection-security-coverage-unavailable");
            }

            if (!defenderHealthy && !firewallHealthy)
            {
                return new ProtectionHealthResult(
                    ProtectionHealthState.Degraded,
                    false,
                    "Windows protection is significantly reduced",
                    $"Microsoft Defender is {snapshot.DefenderStatus} and Windows Firewall is {snapshot.FirewallStatus}.",
                    "Turn on Microsoft Defender and Windows Firewall unless another trusted managed security product is intentionally providing equivalent protection.",
                    "protection-defender-firewall-degraded");
            }

            if (!defenderHealthy)
            {
                return new ProtectionHealthResult(
                    ProtectionHealthState.Degraded,
                    false,
                    "Antivirus protection needs attention",
                    $"Microsoft Defender is {snapshot.DefenderStatus}.",
                    "Turn on Microsoft Defender or confirm that another trusted antivirus product is actively protecting this computer.",
                    "protection-defender-degraded");
            }

            return new ProtectionHealthResult(
                ProtectionHealthState.Degraded,
                false,
                "Firewall protection needs attention",
                $"Windows Firewall is {snapshot.FirewallStatus}.",
                "Turn on Windows Firewall for all network profiles unless another managed firewall is intentionally providing equivalent protection.",
                "protection-firewall-degraded");
        }

        public enum ProtectionHealthState
        {
            Healthy,
            Degraded
        }

        public sealed record ProtectionHealthResult(
            ProtectionHealthState State,
            bool FullyProtected,
            string Title,
            string Summary,
            string RecommendedAction,
            string ReasonCode);
    }
}
