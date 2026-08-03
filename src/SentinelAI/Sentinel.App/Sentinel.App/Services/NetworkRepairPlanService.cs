/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts verified network-health evidence into a conservative repair plan.
    /// This layer is read-only and never changes DNS, Winsock, or adapter state.
    /// </summary>
    public sealed class NetworkRepairPlanService
    {
        private readonly NetworkHealthAssessmentService _assessmentService = new();

        public async Task<NetworkRepairPlan> BuildPlanAsync(
            CancellationToken cancellationToken = default)
        {
            NetworkHealthAssessment assessment =
                await _assessmentService.AssessAsync(cancellationToken).ConfigureAwait(false);

            if (!assessment.RepairInvestigationWarranted)
                return NetworkRepairPlan.NoAction(assessment, assessment.Summary);

            var candidates = new List<NetworkRepairCandidate>();

            // At this stage the only automatically plausible low-risk action is a
            // DNS cache flush, and even that is only planned after Sentinel verifies
            // that the local adapter/gateway path exists while name resolution fails.
            if (assessment.HasActiveAdapter &&
                assessment.HasDefaultGateway &&
                assessment.DnsConfigured &&
                !assessment.DnsResolutionSucceeded)
            {
                candidates.Add(new NetworkRepairCandidate(
                    NetworkRepairAction.FlushDnsCache,
                    "Flush DNS resolver cache",
                    "The network path is present and DNS servers are configured, but name resolution failed during Sentinel's check.",
                    AutomaticEligible: true,
                    RequiresRestart: false));

                // Winsock reset is much more disruptive and normally requires a restart.
                // It is never automatically eligible from a single DNS failure.
                candidates.Add(new NetworkRepairCandidate(
                    NetworkRepairAction.ResetWinsock,
                    "Reset Winsock catalog",
                    "Winsock reset is reserved for persistent, correlated network-stack failures after lower-risk repair fails.",
                    AutomaticEligible: false,
                    RequiresRestart: true));
            }

            if (candidates.Count == 0)
                return NetworkRepairPlan.NoAction(
                    assessment,
                    "Sentinel found network-health evidence but no safe repair action is currently supported.");

            return new NetworkRepairPlan(
                assessment,
                true,
                candidates,
                "Sentinel identified a low-risk network repair candidate. The condition must pass the safety gate before execution.");
        }
    }

    public sealed record NetworkRepairPlan(
        NetworkHealthAssessment Assessment,
        bool ActionWarranted,
        IReadOnlyList<NetworkRepairCandidate> Candidates,
        string Summary)
    {
        public static NetworkRepairPlan NoAction(
            NetworkHealthAssessment assessment,
            string summary) =>
            new(assessment, false, Array.Empty<NetworkRepairCandidate>(), summary);
    }

    public sealed record NetworkRepairCandidate(
        NetworkRepairAction Action,
        string Title,
        string Evidence,
        bool AutomaticEligible,
        bool RequiresRestart);

    public enum NetworkRepairAction
    {
        None,
        FlushDnsCache,
        ResetWinsock
    }
}
