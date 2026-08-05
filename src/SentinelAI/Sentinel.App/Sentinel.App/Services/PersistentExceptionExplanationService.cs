/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;
using System;
using System.Linq;
using System.Text;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Produces plain-language explanations for Ask Sentinel, the dashboard, and
    /// Activity Center from a verified persistent-investigation record.
    /// </summary>
    public sealed class PersistentExceptionExplanationService
    {
        public string BuildAskSentinelExplanation(PersistentInvestigationRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            var text = new StringBuilder();
            text.AppendLine("I previously investigated this condition.");
            text.AppendLine();
            text.AppendLine(record.RootCause);
            text.AppendLine();

            foreach (RepairAttemptRecord attempt in record.RepairAttempts)
            {
                text.AppendLine($"{attempt.RepairPath}: {DescribeOutcome(attempt.Outcome)}");
            }

            text.AppendLine();
            text.AppendLine(record.State == InvestigationLifecycleState.PersistentNoncritical
                ? "No verified repair is currently available, and the remaining condition is classified as noncritical."
                : "The investigation is still active because a repair path or risk decision remains unresolved.");

            if (record.NotificationsSuppressed && record.IsEligibleForSilentMonitoring)
            {
                text.AppendLine();
                text.AppendLine("Nothing material has changed since the last verified investigation, so I am continuing to monitor it silently. I will notify you if the condition changes or a new verified repair becomes available.");
            }

            return text.ToString().Trim();
        }

        public ActivityMessage BuildActivityMessage(PersistentInvestigationRecord record, bool reactivated)
        {
            ArgumentNullException.ThrowIfNull(record);

            if (reactivated)
            {
                return new ActivityMessage(
                    "Persistent investigation reopened",
                    $"Sentinel detected material evidence changes for {record.RootCause}. Silent monitoring ended and the condition requires a new investigation.",
                    true);
            }

            if (record.NotificationsSuppressed && record.IsEligibleForSilentMonitoring)
            {
                return new ActivityMessage(
                    "Monitoring known condition silently",
                    $"Sentinel completed the verified investigation for {record.RootCause}, exhausted applicable repair paths, classified the remaining condition as noncritical, and will continue monitoring for changes.",
                    false);
            }

            return new ActivityMessage(
                "Persistent investigation updated",
                $"Sentinel updated the investigation record for {record.RootCause}. Current state: {record.State}.",
                record.State is InvestigationLifecycleState.Critical
                    or InvestigationLifecycleState.RequiresManualRepair
                    or InvestigationLifecycleState.RequiresUserApproval);
        }

        private static string DescribeOutcome(RepairAttemptOutcome outcome) => outcome switch
        {
            RepairAttemptOutcome.Succeeded => "completed successfully",
            RepairAttemptOutcome.Failed => "attempted but did not resolve the condition",
            RepairAttemptOutcome.Unavailable => "checked; no verified repair was available",
            RepairAttemptOutcome.NotApplicable => "not applicable",
            RepairAttemptOutcome.UserDeclined => "declined by the user",
            RepairAttemptOutcome.AwaitingApproval => "awaiting approval or manual action",
            _ => outcome.ToString()
        };

        public sealed record ActivityMessage(string Title, string Summary, bool RequiresAttention);
    }
}
