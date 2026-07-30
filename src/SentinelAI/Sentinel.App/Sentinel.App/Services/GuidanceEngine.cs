/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    public sealed class GuidanceEngine
    {
        public GuidanceResult Analyze(SystemSnapshot snapshot)
        {
            if (!snapshot.DefenderEnabled)
            {
                return new GuidanceResult(
                    "Microsoft Defender needs attention",
                    "High",
                    "Windows' built-in antivirus protection is not fully active.",
                    "Without active antivirus protection, harmful files and applications may not be detected or blocked.",
                    "Open Windows Security and turn on Microsoft Defender protection. Sentinel AI will verify the status afterward.",
                    "Approval required",
                    "Automatic repair will be added after the remediation safety workflow is complete.");
            }

            if (!snapshot.FirewallEnabled)
            {
                return new GuidanceResult(
                    "Windows Firewall needs attention",
                    "High",
                    "One or more Windows Firewall profiles are not fully enabled.",
                    "The firewall helps block unsolicited network traffic and reduces exposure to network attacks.",
                    "Enable Windows Firewall for all network profiles unless another trusted firewall is managing this computer.",
                    "Approval required",
                    "Automatic repair will be added after the remediation safety workflow is complete.");
            }

            if (Contains(snapshot.LatestEventSource, "WindowsUpdateClient") &&
                Contains(snapshot.LatestEventMessage, "0x80073D02"))
            {
                return new GuidanceResult(
                    "An application update could not finish",
                    "Low",
                    "Windows tried to update an application while files used by that application were still open.",
                    "The computer remains protected, but the application may not receive its newest fixes until the update succeeds.",
                    "Close the application named in the event, then retry the update from Microsoft Store or Windows Update.",
                    "Guided fix available",
                    "Sentinel AI can guide the user through closing the application, retrying the update, and verifying completion.");
            }

            if (snapshot.CriticalEventCount > 0)
            {
                return new GuidanceResult(
                    "Windows reported a critical system event",
                    "High",
                    "Windows recorded a critical event during the last 24 hours.",
                    "Critical events can indicate a serious reliability, hardware, driver, or security problem.",
                    "Review the latest event details before making changes. Avoid restarting critical services or deleting files until the cause is identified.",
                    "Review required",
                    "Sentinel AI will provide a targeted repair only when the event can be matched to a verified procedure.");
            }

            if (snapshot.ErrorEventCount > 0)
            {
                return new GuidanceResult(
                    "Windows reported an error",
                    "Low",
                    HumanizeEvent(snapshot.LatestEventSource, snapshot.LatestEventMessage),
                    "Many Windows errors are temporary, but repeated errors can prevent updates or affect application reliability.",
                    "Follow the recommended action shown here. Sentinel AI will continue monitoring to determine whether the error repeats.",
                    "Guidance available",
                    "No automatic change will be made until a verified and reversible repair procedure is matched.");
            }

            if (snapshot.FlaggedServiceCount > 0)
            {
                return new GuidanceResult(
                    "A Windows service should be reviewed",
                    "Moderate",
                    $"Sentinel AI found a service condition involving {snapshot.PrimaryFlaggedServiceName}.",
                    "Services run in the background and can start automatically, so unusual service locations deserve review.",
                    snapshot.PrimaryFlaggedServiceReason,
                    "Review required",
                    "Stopping or disabling a service requires approval because it may affect Windows or installed applications.");
            }

            if (snapshot.FlaggedProcessCount > 0)
            {
                return new GuidanceResult(
                    "A running application should be reviewed",
                    "Informational",
                    $"Sentinel AI found a process condition involving {snapshot.PrimaryFlaggedProcessName}.",
                    "A flagged location or signature does not automatically mean malware. It means the process deserves additional context.",
                    snapshot.PrimaryFlaggedProcessReason,
                    "Review only",
                    "Sentinel AI will not stop or quarantine a process without user approval and stronger evidence.");
            }

            if (snapshot.MemoryUsagePercent >= 90)
            {
                return new GuidanceResult(
                    "Memory use is very high",
                    "Moderate",
                    $"The computer is using {snapshot.MemoryUsagePercent:0.0}% of available physical memory.",
                    "Very high memory use can cause slow response, application freezes, and increased disk activity.",
                    $"Close unneeded applications and review {snapshot.HighestMemoryProcessName}, currently the highest-memory process.",
                    "Guided fix available",
                    "Sentinel AI can identify applications that are safe to close, but it will ask before closing them.");
            }

            if (snapshot.DiskUsagePercent >= 90)
            {
                return new GuidanceResult(
                    "The system drive is running low on space",
                    "Moderate",
                    $"The system drive is {snapshot.DiskUsagePercent:0.0}% full.",
                    "Low disk space can interrupt Windows updates, prevent applications from saving data, and reduce reliability.",
                    "Remove unneeded files or use Windows Storage settings to clean temporary files.",
                    "Guided fix available",
                    "Sentinel AI can identify safe cleanup categories before deleting anything.");
            }

            return new GuidanceResult(
                "Your computer looks healthy",
                "Low",
                "Core protections are active and no urgent issue currently requires action.",
                "Sentinel AI is continuing to watch Windows events, processes, services, system resources, Defender, and Firewall.",
                "No action is needed right now.",
                "No fix needed",
                "Monitoring will continue automatically.");
        }

        private static string HumanizeEvent(string source, string message)
        {
            if (Contains(source, "WindowsUpdateClient"))
            {
                return "Windows could not complete an update. The event details identify the affected update and the technical error.";
            }

            if (Contains(source, "Service Control Manager"))
            {
                return "A Windows background service did not start, stopped unexpectedly, or changed state.";
            }

            return string.IsNullOrWhiteSpace(message)
                ? "Windows recorded an error that should be reviewed."
                : message;
        }

        private static bool Contains(string? value, string text) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(text, StringComparison.OrdinalIgnoreCase);

        public sealed record GuidanceResult(
            string Title,
            string Severity,
            string WhatHappened,
            string WhyItMatters,
            string RecommendedAction,
            string FixAvailability,
            string FixDetails);
    }
}
