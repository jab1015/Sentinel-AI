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
            string combined = $"{item.Category} {item.Summary}";

            if (ContainsAny(combined, "defragment", "retrim", "drive optimization", "storage optimization"))
                return FriendlyValueSummaryService.ValueActionKind.DriveOptimization;

            if (ContainsAny(combined, "temporary file", "stale temporary", "disk space"))
                return FriendlyValueSummaryService.ValueActionKind.TemporaryFileCleanup;

            if (ContainsAny(combined, "network", "winsock", "dns"))
                return FriendlyValueSummaryService.ValueActionKind.NetworkRepair;

            if (ContainsAny(combined, "system file", "system image", "dism", "sfc"))
                return FriendlyValueSummaryService.ValueActionKind.SystemFileRepair;

            if (ContainsAny(combined, "driver"))
                return FriendlyValueSummaryService.ValueActionKind.DriverRepair;

            if (ContainsAny(combined, "startup"))
                return FriendlyValueSummaryService.ValueActionKind.StartupOptimization;

            if (item.Category.Equals("Automatic Repair", StringComparison.OrdinalIgnoreCase))
                return FriendlyValueSummaryService.ValueActionKind.SecurityRepair;

            return null;
        }

        private static bool ContainsAny(string value, params string[] terms) =>
            terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
