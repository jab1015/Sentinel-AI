/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts a verified persistent-investigation record into a user-facing
    /// presentation decision. This policy never disables monitoring; it only
    /// determines whether the current condition should interrupt the user.
    /// </summary>
    public sealed class PersistentExceptionPresentationService
    {
        public PresentationDecision Evaluate(PersistentInvestigationRecord? record)
        {
            if (record is null)
            {
                return PresentationDecision.CreateActiveFinding(
                    "No reusable verified investigation exists for this condition.");
            }

            if (record.IsCritical || record.State == InvestigationLifecycleState.Critical)
            {
                return PresentationDecision.CreateActiveFinding(
                    "Critical conditions cannot be hidden or silenced.");
            }

            if (record.State != InvestigationLifecycleState.PersistentNoncritical)
            {
                return PresentationDecision.CreateActiveFinding(
                    "The investigation is not complete enough to qualify as a persistent noncritical exception.");
            }

            if (!record.HasExhaustedRepairLedger)
            {
                return PresentationDecision.CreateActiveFinding(
                    "Applicable repair paths have not been fully exhausted.");
            }

            if (!record.NotificationsSuppressed)
            {
                return PresentationDecision.CreateKnownCondition(
                    "Sentinel completed a verified investigation and found no remaining safe repair, but notifications are still enabled.",
                    "Monitor Silently");
            }

            return PresentationDecision.CreateSilentMonitoring(
                "Sentinel previously completed a verified investigation. No material evidence has changed and no new verified repair is available.",
                "Resume Notifications");
        }

        public sealed record PresentationDecision(
            bool ShowAsActiveFinding,
            bool ShowKnownCondition,
            bool SuppressNotification,
            bool ContinueMonitoring,
            string Title,
            string Summary,
            string ActionLabel)
        {
            public static PresentationDecision CreateActiveFinding(string summary) => new(
                ShowAsActiveFinding: true,
                ShowKnownCondition: false,
                SuppressNotification: false,
                ContinueMonitoring: true,
                Title: "Condition requires attention",
                Summary: summary,
                ActionLabel: string.Empty);

            public static PresentationDecision CreateKnownCondition(string summary, string actionLabel) => new(
                ShowAsActiveFinding: false,
                ShowKnownCondition: true,
                SuppressNotification: false,
                ContinueMonitoring: true,
                Title: "Known persistent condition",
                Summary: summary,
                ActionLabel: actionLabel);

            public static PresentationDecision CreateSilentMonitoring(string summary, string actionLabel) => new(
                ShowAsActiveFinding: false,
                ShowKnownCondition: true,
                SuppressNotification: true,
                ContinueMonitoring: true,
                Title: "Monitoring known condition silently",
                Summary: summary,
                ActionLabel: actionLabel);
        }
    }
}
