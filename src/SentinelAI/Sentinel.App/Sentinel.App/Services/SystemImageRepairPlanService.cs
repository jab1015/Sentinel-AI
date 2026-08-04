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
    /// Converts verified DISM/SFC health evidence into a conservative repair plan.
    /// This layer never repairs Windows. Component-store repair is planned before
    /// protected-file repair so SFC can use a healthy Windows image as its source.
    /// </summary>
    public sealed class SystemImageRepairPlanService
    {
        private readonly SystemImageHealthAssessmentService _assessmentService = new();

        public async Task<SystemImageRepairPlan> BuildPlanAsync(
            CancellationToken cancellationToken = default)
        {
            SystemImageHealthAssessment assessment =
                await _assessmentService.AssessAsync(cancellationToken).ConfigureAwait(false);

            if (!assessment.RepairInvestigationWarranted)
                return SystemImageRepairPlan.NoAction(assessment, assessment.Summary);

            var candidates = new List<SystemImageRepairCandidate>();

            if (assessment.ComponentStoreCorruptionDetected)
            {
                candidates.Add(new SystemImageRepairCandidate(
                    SystemImageRepairAction.RestoreComponentStore,
                    "Repair Windows component store",
                    "DISM ScanHealth reported component-store corruption. RestoreHealth is the Windows-native repair action for this condition.",
                    AutomaticEligible: true,
                    RequiresElevation: true,
                    RequiresRestart: false));
            }

            if (assessment.ProtectedFilesCorruptionDetected)
            {
                candidates.Add(new SystemImageRepairCandidate(
                    SystemImageRepairAction.RepairProtectedFiles,
                    "Repair protected Windows files",
                    assessment.ComponentStoreCorruptionDetected
                        ? "SFC reported protected-file integrity problems. Sentinel must repair and verify the component store first, then run SFC repair."
                        : "SFC VerifyOnly reported protected-file integrity problems while the component store did not report repairable corruption.",
                    AutomaticEligible: !assessment.ComponentStoreCorruptionDetected,
                    RequiresElevation: true,
                    RequiresRestart: false));
            }

            if (candidates.Count == 0)
            {
                return SystemImageRepairPlan.NoAction(
                    assessment,
                    "Sentinel detected incomplete system-integrity evidence but no verified Windows repair action is safe to plan automatically.");
            }

            return new SystemImageRepairPlan(
                assessment,
                true,
                candidates,
                assessment.ComponentStoreCorruptionDetected
                    ? "Sentinel verified component-store corruption. Windows image repair must occur before any protected-file repair."
                    : "Sentinel verified protected-system-file corruption and identified the Windows-native repair action.");
        }
    }

    public sealed record SystemImageRepairPlan(
        SystemImageHealthAssessment Assessment,
        bool ActionWarranted,
        IReadOnlyList<SystemImageRepairCandidate> Candidates,
        string Summary)
    {
        public static SystemImageRepairPlan NoAction(
            SystemImageHealthAssessment assessment,
            string summary) =>
            new(assessment, false, Array.Empty<SystemImageRepairCandidate>(), summary);
    }

    public sealed record SystemImageRepairCandidate(
        SystemImageRepairAction Action,
        string Title,
        string Evidence,
        bool AutomaticEligible,
        bool RequiresElevation,
        bool RequiresRestart);

    public enum SystemImageRepairAction
    {
        None,
        RestoreComponentStore,
        RepairProtectedFiles
    }
}
