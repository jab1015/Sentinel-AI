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
    /// Converts verified maintenance/remediation outcomes into short, friendly,
    /// nontechnical messages that make Sentinel's completed work visible.
    /// This service never invents work: only actions explicitly marked as both
    /// completed and verified are eligible for a user-facing value summary.
    /// </summary>
    public sealed class FriendlyValueSummaryService
    {
        public FriendlyValueSummary? CreateSummary(IEnumerable<VerifiedValueAction> actions)
        {
            ArgumentNullException.ThrowIfNull(actions);

            VerifiedValueAction[] verified = actions
                .Where(action => action.Completed && action.Verified)
                .GroupBy(action => action.Kind)
                .Select(group => group.Last())
                .ToArray();

            if (verified.Length == 0)
                return null;

            string[] descriptions = verified
                .Select(Describe)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            if (descriptions.Length == 0)
                return null;

            bool repairedProblem = verified.Any(action => action.ProblemFoundAndResolved);
            string title = repairedProblem
                ? "I found something and took care of it."
                : verified.Length == 1
                    ? "A little housekeeping is done."
                    : "I gave your computer a quick tune-up.";

            string work = JoinFriendly(descriptions);
            string ending = repairedProblem
                ? "I checked again after the work was finished."
                : "Everything I completed checked out successfully.";

            return new FriendlyValueSummary(
                title,
                $"{work} {ending}",
                verified.Select(action => action.Kind).ToArray());
        }

        private static string Describe(VerifiedValueAction action) => action.Kind switch
        {
            ValueActionKind.DriveOptimization => "I optimized your drive",
            ValueActionKind.DiskCheck => "I checked your drive for problems",
            ValueActionKind.TemporaryFileCleanup => "I cleaned up unnecessary temporary files",
            ValueActionKind.SystemFileRepair => action.ProblemFoundAndResolved
                ? "I repaired Windows system files that needed attention"
                : "I checked Windows system files",
            ValueActionKind.NetworkRepair => "I repaired and checked your network settings",
            ValueActionKind.SecurityRepair => "I corrected a Windows security setting and verified the protection is back on",
            ValueActionKind.DriverRepair => "I completed a driver repair and verified the result",
            ValueActionKind.StartupOptimization => "I tidied up the startup configuration",
            _ => string.Empty
        };

        private static string JoinFriendly(IReadOnlyList<string> items)
        {
            if (items.Count == 1)
                return items[0] + ".";

            if (items.Count == 2)
                return $"{items[0]} and {LowerFirst(items[1])}.";

            return string.Join(", ", items.Take(items.Count - 1)) +
                   $", and {LowerFirst(items[^1])}.";
        }

        private static string LowerFirst(string value)
        {
            if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
                return value;

            return char.ToLowerInvariant(value[0]) + value[1..];
        }

        public sealed record VerifiedValueAction(
            ValueActionKind Kind,
            bool Completed,
            bool Verified,
            bool ProblemFoundAndResolved = false);

        public sealed record FriendlyValueSummary(
            string Title,
            string Message,
            IReadOnlyList<ValueActionKind> VerifiedActions);

        public enum ValueActionKind
        {
            DriveOptimization,
            DiskCheck,
            TemporaryFileCleanup,
            SystemFileRepair,
            NetworkRepair,
            SecurityRepair,
            DriverRepair,
            StartupOptimization
        }
    }
}
