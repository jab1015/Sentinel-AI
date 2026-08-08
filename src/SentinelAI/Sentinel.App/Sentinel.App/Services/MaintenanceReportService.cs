/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Builds a concise user-facing maintenance report from the retained history.
    /// Healthy systems remain quiet; technical detail is retained only for explicit
    /// diagnostics and is not surfaced in the normal summary.
    /// </summary>
    public sealed class MaintenanceReportService
    {
        private readonly MaintenanceHistoryService _historyService = new();

        public MaintenanceReport BuildReport()
        {
            MaintenanceHistorySummary history = _historyService.GetSummary();

            if (!history.HistoryAvailable)
            {
                return new MaintenanceReport(
                    MaintenanceReportState.NeedsAttention,
                    "Maintenance history unavailable",
                    "Sentinel could not read its verified maintenance history and will not claim that no prior action occurred.",
                    Array.Empty<MaintenanceReportItem>(),
                    false);
            }

            if (history.TotalActions == 0)
            {
                return new MaintenanceReport(
                    MaintenanceReportState.Healthy,
                    "No maintenance needed",
                    "Sentinel has not needed to perform any maintenance recently.",
                    Array.Empty<MaintenanceReportItem>(),
                    false);
            }

            MaintenanceReportItem[] items = history.Entries
                .Take(10)
                .Select(ToReportItem)
                .ToArray();

            bool followUpRequired = history.FailedActions > 0;
            MaintenanceReportState state = followUpRequired
                ? MaintenanceReportState.NeedsAttention
                : history.RolledBackActions > 0
                    ? MaintenanceReportState.Protected
                    : MaintenanceReportState.Healthy;

            string headline = followUpRequired
                ? "Maintenance needs attention"
                : history.RolledBackActions > 0
                    ? "Sentinel protected your system"
                    : "Maintenance is up to date";

            string summary = followUpRequired
                ? $"Sentinel attempted recent maintenance, but {history.FailedActions} action(s) did not complete with verified success and may need your attention."
                : history.RolledBackActions > 0
                    ? $"Sentinel safely handled recent maintenance and restored {history.RolledBackActions} change(s) when verification did not pass."
                    : $"Sentinel handled {history.TotalActions} maintenance action(s) recently and verified the completed changes.";

            return new MaintenanceReport(state, headline, summary, items, followUpRequired);
        }

        private static MaintenanceReportItem ToReportItem(MaintenanceHistoryEntry entry)
        {
            string outcome = entry.RolledBack
                ? "Safely restored"
                : entry.Verified
                    ? "Verified"
                    : entry.Successful
                        ? "Completed"
                        : entry.Attempted
                            ? "Needs attention"
                            : "No change needed";

            return new MaintenanceReportItem(
                entry.TimestampUtc,
                entry.Category,
                entry.UserSummary,
                outcome,
                entry.Attempted && !entry.Successful && !entry.RolledBack)
            {
                Action = entry.Action
            };
        }
    }

    public sealed record MaintenanceReport(
        MaintenanceReportState State,
        string Headline,
        string Summary,
        IReadOnlyList<MaintenanceReportItem> RecentItems,
        bool UserActionRequired);

    public sealed record MaintenanceReportItem(
        DateTimeOffset TimestampUtc,
        string Category,
        string Summary,
        string Outcome,
        bool NeedsAttention)
    {
        // Retain the exact recorded action so presentation logic can distinguish an
        // actual repair from research, detection, scanning, or an uncompleted attempt.
        public string Action { get; init; } = string.Empty;
    }

    public enum MaintenanceReportState
    {
        Healthy,
        Protected,
        NeedsAttention
    }
}
