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
                return Result(
                    "Microsoft Defender needs attention", "High",
                    "Windows' built-in antivirus protection is not fully active.",
                    "Without active antivirus protection, harmful files and applications may not be detected or blocked.",
                    "Open Windows Security and turn on Microsoft Defender protection. Sentinel AI will verify the status afterward.",
                    "Approval required",
                    "Review the protection settings before making a change.",
                    "open-windows-security", "Open Windows Security");
            }

            if (!snapshot.FirewallEnabled)
            {
                return Result(
                    "Windows Firewall needs attention", "High",
                    "One or more Windows Firewall profiles are not fully enabled.",
                    "The firewall helps block unsolicited network traffic and reduces exposure to network attacks.",
                    "Review Windows Firewall and enable protection unless another trusted firewall is managing this computer.",
                    "Approval required",
                    "Sentinel AI will open the correct Windows settings page and verify the status after the user makes the change.",
                    "open-firewall", "Review Firewall");
            }

            if (Contains(snapshot.LatestEventSource, "WindowsUpdateClient") &&
                Contains(snapshot.LatestEventMessage, "0x80073D02"))
            {
                return Result(
                    "An application update could not finish", "Low",
                    "Windows tried to update an application while files used by that application were still open.",
                    "The computer remains protected, but the application may not receive its newest fixes until the update succeeds.",
                    "Close the application named in the event, then retry the update from Microsoft Store or Windows Update.",
                    "Guided fix available",
                    "Open Windows Update, retry the installation, and let Sentinel AI verify whether the error returns.",
                    "open-windows-update", "Open Windows Update");
            }

            if (IsRepeatedServiceFailure(snapshot))
            {
                string serviceName = ExtractServiceDisplayName(snapshot.LatestEventMessage);
                string repetition = Contains(snapshot.LatestEventMessage, "time(s)")
                    ? " Windows reports that this has happened repeatedly."
                    : string.Empty;

                return Result(
                    $"{serviceName} stopped repeatedly", "Moderate",
                    $"The Windows service '{serviceName}' terminated unexpectedly.{repetition}",
                    "A repeated service failure can affect the Windows feature or application that depends on that service. It does not automatically mean the computer is infected.",
                    "Open Windows Services, locate the named service, and review whether it is running. Restart it only after confirming that no storage, update, or application task is currently using it.",
                    "Guided fix available",
                    "Sentinel AI will open Services without changing anything. After review, use Check Again to verify whether the failure continues.",
                    "open-services", "Open Windows Services");
            }

            if (snapshot.CriticalEventCount > 0)
            {
                return Result(
                    "Windows reported a critical system event", "High",
                    "Windows recorded a critical event during the last 24 hours.",
                    "Critical events can indicate a serious reliability, hardware, driver, or security problem.",
                    "Review the latest event details before making changes. Avoid restarting critical services or deleting files until the cause is identified.",
                    "Review required",
                    "Sentinel AI will provide a targeted repair only when the event can be matched to a verified procedure.");
            }

            if (snapshot.ErrorEventCount > 0)
            {
                return Result(
                    "Windows reported an error", "Low",
                    HumanizeEvent(snapshot.LatestEventSource, snapshot.LatestEventMessage),
                    "Many Windows errors are temporary, but repeated errors can prevent updates or affect application reliability.",
                    "Follow the recommended action shown here. Sentinel AI will continue monitoring to determine whether the error repeats.",
                    "Guidance available",
                    "No automatic change will be made until a verified and reversible repair procedure is matched.",
                    "check-again", "Check Again");
            }

            if (snapshot.FlaggedServiceCount > 0)
            {
                return Result(
                    "A Windows service should be reviewed", "Moderate",
                    $"Sentinel AI found a service condition involving {snapshot.PrimaryFlaggedServiceName}.",
                    "Services run in the background and can start automatically, so unusual service locations deserve review.",
                    snapshot.PrimaryFlaggedServiceReason,
                    "Review required",
                    "Stopping or disabling a service requires approval because it may affect Windows or installed applications.",
                    "open-services", "Open Windows Services");
            }

            if (snapshot.FlaggedProcessCount > 0)
            {
                return Result(
                    "A running application should be reviewed", "Informational",
                    $"Sentinel AI found a process condition involving {snapshot.PrimaryFlaggedProcessName}.",
                    "A flagged location or signature does not automatically mean malware. It means the process deserves additional context.",
                    snapshot.PrimaryFlaggedProcessReason,
                    "Review only",
                    "Sentinel AI will not stop or quarantine a process without user approval and stronger evidence.",
                    "open-task-manager", "Open Task Manager");
            }

            if (snapshot.MemoryUsagePercent >= 90)
            {
                return Result(
                    "Memory use is very high", "Moderate",
                    $"The computer is using {snapshot.MemoryUsagePercent:0.0}% of available physical memory.",
                    "Very high memory use can cause slow response, application freezes, and increased disk activity.",
                    $"Close unneeded applications and review {snapshot.HighestMemoryProcessName}, currently the highest-memory process.",
                    "Guided fix available",
                    "Sentinel AI can open Task Manager so the user can review applications before closing anything.",
                    "open-task-manager", "Open Task Manager");
            }

            if (snapshot.DiskUsagePercent >= 90)
            {
                return Result(
                    "The system drive is running low on space", "Moderate",
                    $"The system drive is {snapshot.DiskUsagePercent:0.0}% full.",
                    "Low disk space can interrupt Windows updates, prevent applications from saving data, and reduce reliability.",
                    "Review Windows Storage settings and remove only files you recognize or safe temporary-file categories.",
                    "Guided fix available",
                    "Sentinel AI will open Storage settings without deleting anything.",
                    "open-storage", "Open Storage Settings");
            }

            return Result(
                "Your computer looks healthy", "Low",
                "Core protections are active and no urgent issue currently requires action.",
                "Sentinel AI is continuing to watch Windows events, processes, services, system resources, Defender, and Firewall.",
                "No action is needed right now.",
                "No fix needed",
                "Monitoring will continue automatically.",
                "check-again", "Check Again");
        }

        private static bool IsRepeatedServiceFailure(SystemSnapshot snapshot) =>
            Contains(snapshot.LatestEventSource, "Service Control Manager") &&
            Contains(snapshot.LatestEventMessage, "terminated unexpectedly");

        private static string ExtractServiceDisplayName(string message)
        {
            const string prefix = "The ";
            const string marker = " service terminated unexpectedly";

            int start = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            int end = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start + prefix.Length)
            {
                return message.Substring(start + prefix.Length, end - start - prefix.Length).Trim();
            }

            return "A Windows service";
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

        private static GuidanceResult Result(
            string title,
            string severity,
            string whatHappened,
            string whyItMatters,
            string recommendedAction,
            string fixAvailability,
            string fixDetails,
            string actionId = "",
            string actionLabel = "") =>
            new(title, severity, whatHappened, whyItMatters, recommendedAction,
                fixAvailability, fixDetails, actionId, actionLabel);

        public sealed record GuidanceResult(
            string Title,
            string Severity,
            string WhatHappened,
            string WhyItMatters,
            string RecommendedAction,
            string FixAvailability,
            string FixDetails,
            string ActionId,
            string ActionLabel);
    }
}
