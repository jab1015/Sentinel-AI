/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    public sealed class RiskAssessmentEngine
    {
        public RiskAssessment Assess(SystemSnapshot snapshot)
        {
            int score = 0;
            string recommendation = "No immediate action is required. Continue normal monitoring.";
            bool defenderUnavailable =
                snapshot.DefenderStatus.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) ||
                snapshot.DefenderStatus.Equals("Loading...", StringComparison.OrdinalIgnoreCase);
            bool firewallUnavailable =
                snapshot.FirewallStatus.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) ||
                snapshot.FirewallStatus.Equals("Loading...", StringComparison.OrdinalIgnoreCase);
            bool advancedSecurityEntitled =
                !string.Equals(snapshot.NetworkConnectionMonitoringStatus, "Subscription required", StringComparison.OrdinalIgnoreCase);
            bool monitoringCoverageIncomplete =
                defenderUnavailable ||
                firewallUnavailable ||
                (advancedSecurityEntitled && (
                !snapshot.NetworkConnectionMonitoringAvailable ||
                !snapshot.AuthenticationMonitoringAvailable ||
                !snapshot.EventLogMonitoringAvailable ||
                !snapshot.ProcessMonitoringAvailable ||
                !snapshot.CommandLineMonitoringAvailable ||
                !snapshot.ProcessLineageMonitoringAvailable ||
                !snapshot.ServiceMonitoringAvailable ||
                !snapshot.StartupPersistenceMonitoringAvailable ||
                !snapshot.ScheduledTaskMonitoringAvailable ||
                snapshot.SpywareCorrelationState.Equals("EvidenceIncomplete", StringComparison.OrdinalIgnoreCase)));

            if (!snapshot.DefenderEnabled)
            {
                score += defenderUnavailable ? 15 : 40;
                recommendation = defenderUnavailable
                    ? "Sentinel could not verify current Microsoft Defender state. Keep monitoring active and review Windows Security if verification does not recover."
                    : "Turn on Microsoft Defender or confirm that another trusted antivirus product is actively protecting this computer.";
            }

            if (!snapshot.FirewallEnabled)
            {
                score += firewallUnavailable ? 15 : 35;
                recommendation = firewallUnavailable
                    ? "Sentinel could not verify complete Windows Firewall profile state. Keep monitoring active and review Windows Security if verification does not recover."
                    : "Turn on Windows Firewall for all network profiles unless another managed firewall is providing equivalent protection.";
            }

            if (snapshot.AuthenticationAnomalyDetected)
            {
                score += 45;
                recommendation = "Review the repeated failed-logon source and confirm whether the attempts were expected. Sentinel will continue monitoring before recommending containment.";
            }

            score += Math.Min(snapshot.CriticalEventCount * 12, 24);
            score += Math.Min(snapshot.ErrorEventCount * 2, 16);

            bool hasCriticalEvent = snapshot.CriticalEventCount > 0;
            bool repeatedServiceFailure =
                Contains(snapshot.LatestEventSource, "Service Control Manager") &&
                Contains(snapshot.LatestEventMessage, "terminated unexpectedly");

            if (hasCriticalEvent)
            {
                recommendation = "Review the latest critical event details before making changes. Sentinel will continue correlating the event with current system evidence and will recommend a targeted repair only when the cause is verified.";
            }

            if (repeatedServiceFailure)
            {
                score += 8;
                recommendation = "A Windows service has stopped unexpectedly. Review the named service and check whether the failure repeats before changing its startup settings.";
            }

            if (snapshot.SpywareCorrelationState.Equals("HighConcern", StringComparison.OrdinalIgnoreCase))
            {
                score += 45;
                recommendation = "Sentinel correlated multiple independent spyware-like behaviors. Investigate the identified process and its persistence and network activity before allowing it to continue.";
            }
            else if (snapshot.SpywareCorrelationState.Equals("Review", StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
                recommendation = "Sentinel correlated multiple unusual behaviors that require review. Verify the responsible process and related persistence or network evidence before taking containment action.";
            }
            else if (snapshot.SpywareCorrelationState.Equals("Observe", StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
            }

            if (snapshot.MemoryUsagePercent >= 90)
            {
                score += 10;
                recommendation = "Memory use is very high. Close unneeded applications and review the highest-memory process.";
            }
            else if (snapshot.MemoryUsagePercent >= 80)
            {
                score += 5;
                if (score < 20)
                {
                    recommendation = "Memory use is elevated. Review running applications if the computer feels slow.";
                }
            }

            if (snapshot.DiskUsagePercent >= 95)
            {
                score += 15;
                recommendation = "The system drive is nearly full. Free storage space to reduce reliability and update risks.";
            }
            else if (snapshot.DiskUsagePercent >= 85)
            {
                score += 8;
                if (score < 20)
                {
                    recommendation = "Available disk space is becoming limited. Consider removing temporary or unneeded files.";
                }
            }

            score = Math.Clamp(score, 0, 100);

            string level = score switch
            {
                >= 70 => "High",
                >= 35 => "Elevated",
                >= 15 => "Moderate",
                _ => "Low"
            };

            string summary = snapshot.AuthenticationAnomalyDetected
                ? snapshot.AuthenticationAnomalySummary
                : snapshot.SpywareCorrelationState.Equals("HighConcern", StringComparison.OrdinalIgnoreCase)
                ? "Multiple independent behaviors correlate into a high-confidence spyware-like concern that requires investigation."
                : snapshot.SpywareCorrelationState.Equals("Review", StringComparison.OrdinalIgnoreCase)
                    ? "Multiple independent unusual behaviors overlap and should be investigated."
                    : monitoringCoverageIncomplete
                        ? "Sentinel cannot verify a healthy security state because one or more monitoring evidence sources are unavailable."
                    : hasCriticalEvent
                        ? "Windows reported a critical system event that requires review. Sentinel is correlating it with current system evidence before recommending any change."
                        : level switch
                        {
                            "High" => "Important security or reliability conditions need attention.",
                            "Elevated" => "One or more conditions should be reviewed soon.",
                            "Moderate" => "The computer is generally protected, with a few items worth reviewing.",
                            _ => repeatedServiceFailure
                                ? "Core protections are active, but a repeated Windows service failure should be reviewed."
                                : "Core protections are active and no major warning conditions were detected."
                        };

            return new RiskAssessment(score, level, summary, recommendation);
        }

        private static bool Contains(string? value, string text) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(text, StringComparison.OrdinalIgnoreCase);

        public sealed record RiskAssessment(
            int Score,
            string Level,
            string Summary,
            string Recommendation);
    }
}
