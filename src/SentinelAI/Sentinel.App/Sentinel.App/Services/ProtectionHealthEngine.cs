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
            bool advancedSecurityNotEntitled =
                string.Equals(snapshot.NetworkConnectionMonitoringStatus, "Subscription required", StringComparison.OrdinalIgnoreCase);

            // Subscription-gated advanced collectors are intentionally inactive, not
            // failed. Evaluate the free tier only against the basic Windows protection
            // signals it actually includes.
            if (advancedSecurityNotEntitled && defenderHealthy && firewallHealthy)
            {
                return new ProtectionHealthResult(
                    ProtectionHealthState.Healthy,
                    false,
                    "Basic Windows protection is active",
                    "Microsoft Defender and Windows Firewall are active. Advanced Sentinel security correlation and proactive protection require an active subscription.",
                    "No basic Windows protection action is required.",
                    "basic-protection-healthy-subscription-required");
            }

            bool monitoringCoverageHealthy =
                snapshot.AuthenticationMonitoringAvailable &&
                snapshot.EventLogMonitoringAvailable &&
                snapshot.ProcessMonitoringAvailable &&
                snapshot.CommandLineMonitoringAvailable &&
                snapshot.ProcessLineageMonitoringAvailable &&
                snapshot.ServiceMonitoringAvailable &&
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
                    "Sentinel network, authentication, Windows Event Log, process, command-line, process-lineage, service, startup-persistence, and scheduled-task monitoring are active. Microsoft Defender and Windows Firewall are also active.",
                    "No action is required.",
                    "protection-healthy");
            }

            // Core Windows protections are verified conditions rather than collector
            // readiness signals. If Windows actually reports Defender or Firewall as
            // disabled/inactive, surface that immediately even while other evidence is
            // still being gathered.
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

            if (!firewallHealthy)
            {
                return new ProtectionHealthResult(
                    ProtectionHealthState.Degraded,
                    false,
                    "Firewall protection needs attention",
                    $"Windows Firewall is {snapshot.FirewallStatus}.",
                    "Turn on Windows Firewall for all network profiles unless another managed firewall is intentionally providing equivalent protection.",
                    "protection-firewall-degraded");
            }

            // Collector availability booleans tell us whether an evidence source has
            // produced an authoritative result on this pass; they do not distinguish a
            // collector that is still initializing/retrying from a collector that is
            // actually broken. Treating a false readiness bit as a verified Sentinel
            // failure caused a temporary red "coverage degraded" investigation during
            // normal startup. Do not manufacture a failure from missing evidence.
            //
            // Keep the product in a neutral Gathering state for as long as one of these
            // sources is still coming online. Real Windows protection failures above,
            // and corroborated threat/investigation evidence elsewhere, still surface
            // immediately. Once the collectors report authoritative evidence this state
            // naturally resolves without an arbitrary startup timeout.
            if (!networkHealthy || !monitoringCoverageHealthy)
            {
                return new ProtectionHealthResult(
                    ProtectionHealthState.Gathering,
                    false,
                    "Gathering security information",
                    "Sentinel is collecting and refreshing the remaining security-monitoring information.",
                    "No action is needed while Sentinel finishes these checks. Monitoring will retry automatically and the status will update as evidence becomes available.",
                    "protection-gathering");
            }

            // All cases above are exhaustive, but keep a fail-closed fallback if a new
            // component is added without a corresponding explanation. This wording does
            // not claim that an unavailable collector is itself a Sentinel failure.
            return new ProtectionHealthResult(
                ProtectionHealthState.Gathering,
                false,
                "Gathering security information",
                "Sentinel is refreshing the information needed to complete the current protection check.",
                "No action is needed while Sentinel retries the remaining checks.",
                "protection-gathering");
        }

        public enum ProtectionHealthState
        {
            Healthy,
            Gathering,
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
