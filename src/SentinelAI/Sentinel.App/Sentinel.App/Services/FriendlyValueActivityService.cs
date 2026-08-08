/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Linq;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts retained verified maintenance history into the friendly value
    /// summaries shown in Sentinel's normal Activity Center experience.
    /// </summary>
    public sealed class FriendlyValueActivityService
    {
        private readonly FriendlyValueSummaryService _summaryService = new();

        public FriendlyValueSummaryService.FriendlyValueSummary? CreateFor(MaintenanceReportItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (!item.Outcome.Equals("Verified", StringComparison.OrdinalIgnoreCase))
                return null;

            FriendlyValueSummaryService.ValueActionKind? kind = Map(item);
            if (!kind.HasValue)
                return null;

            bool resolvedProblem =
                item.Category.Equals("Automatic Repair", StringComparison.OrdinalIgnoreCase) ||
                item.Category.Equals("Network", StringComparison.OrdinalIgnoreCase);

            return _summaryService.CreateSummary(new[]
            {
                new FriendlyValueSummaryService.VerifiedValueAction(
                    kind.Value,
                    Completed: true,
                    Verified: true,
                    ProblemFoundAndResolved: resolvedProblem)
            });
        }

        private static FriendlyValueSummaryService.ValueActionKind? Map(MaintenanceReportItem item)
        {
            string combined = $"{item.Category} {item.Action} {item.Summary}";

            if (ContainsAny(combined, "defragment", "retrim", "drive optimization", "storage optimization"))
                return FriendlyValueSummaryService.ValueActionKind.DriveOptimization;

            if (ContainsAny(combined, "temporary file", "stale temporary", "disk space"))
                return FriendlyValueSummaryService.ValueActionKind.TemporaryFileCleanup;

            if (IsVerifiedNetworkRepair(item))
                return FriendlyValueSummaryService.ValueActionKind.NetworkRepair;

            // Investigations and monitoring summaries may mention networking without
            // changing any setting. Never turn those records into a repair claim.
            if (ContainsAny(combined, "network", "winsock", "dns"))
                return null;

            if (ContainsAny(combined, "system file", "system image", "dism", "sfc"))
                return FriendlyValueSummaryService.ValueActionKind.SystemFileRepair;

            // Never infer a completed driver repair merely because a verified history
            // summary mentions a driver. A repair claim requires an explicitly recorded
            // driver action plus evidence that installation and post-change verification
            // both succeeded. This prevents research, detection, and Windows Update scans
            // from being celebrated as repairs.
            if (IsVerifiedDriverRepair(item))
                return FriendlyValueSummaryService.ValueActionKind.DriverRepair;

            if (ContainsAny(combined, "driver"))
                return null;

            if (ContainsAny(combined, "startup"))
                return FriendlyValueSummaryService.ValueActionKind.StartupOptimization;

            if (item.Category.Equals("Automatic Repair", StringComparison.OrdinalIgnoreCase))
                return FriendlyValueSummaryService.ValueActionKind.SecurityRepair;

            return null;
        }

        private static bool IsVerifiedNetworkRepair(MaintenanceReportItem item)
        {
            if (!item.Category.Equals("Network", StringComparison.OrdinalIgnoreCase))
                return false;

            string action = item.Action ?? string.Empty;
            string summary = item.Summary ?? string.Empty;
            bool explicitRepairAction = ContainsAny(action, "FlushDnsCache", "flush dns", "network repair", "winsock reset");
            bool completed = ContainsAny(summary, "repaired", "repair completed");
            bool postVerified = ContainsAny(summary, "verified", "name resolution is working", "connectivity healthy");
            return explicitRepairAction && completed && postVerified;
        }

        private static bool IsVerifiedDriverRepair(MaintenanceReportItem item)
        {
            string action = item.Action ?? string.Empty;
            string summary = item.Summary ?? string.Empty;

            bool explicitDriverAction = ContainsAny(action, "install driver", "driver install", "repair driver", "driver repair");
            bool installed = ContainsAny(summary, "installed", "installation completed", "repair completed");
            bool postVerified = ContainsAny(summary, "post-repair verification passed", "device verified healthy", "verified after installation");
            bool identified = ContainsAny(summary, "device:", "driver:", "package:");

            return explicitDriverAction && installed && postVerified && identified;
        }

        private static bool ContainsAny(string value, params string[] terms) =>
            terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
