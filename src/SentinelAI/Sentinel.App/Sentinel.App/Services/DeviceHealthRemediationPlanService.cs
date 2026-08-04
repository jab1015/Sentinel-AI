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
    /// Converts verified Plug and Play device problems into conservative remediation
    /// guidance. Driver removal, rollback, disablement, and unattended driver updates
    /// are intentionally outside automatic optimization scope.
    /// </summary>
    public sealed class DeviceHealthRemediationPlanService
    {
        private readonly DeviceHealthAssessmentService _assessmentService = new();

        public DeviceHealthRemediationPlan BuildPlan()
        {
            DeviceHealthAssessment assessment = _assessmentService.Assess();

            if (!assessment.DeviceHealthVerified ||
                !assessment.RepairInvestigationWarranted ||
                assessment.Problems.Count == 0)
            {
                return DeviceHealthRemediationPlan.NoAction(assessment, assessment.Summary);
            }

            var candidates = new List<DeviceHealthRemediationCandidate>();

            foreach (DeviceProblemEvidence device in assessment.Problems.Take(5))
            {
                string deviceName = string.IsNullOrWhiteSpace(device.DeviceDescription)
                    ? device.InstanceId
                    : device.DeviceDescription;

                candidates.Add(new DeviceHealthRemediationCandidate(
                    DeviceHealthRemediationAction.RecommendWindowsUpdateDriverCheck,
                    device.InstanceId,
                    deviceName,
                    device.ProblemCode,
                    "Check Windows Update for a verified driver repair",
                    $"Windows reports a Plug and Play problem for {deviceName} (problem code {device.ProblemCode}). The safest first repair path is a Windows-provided driver update when available.",
                    AutomaticEligible: false,
                    Destructive: false));

                candidates.Add(new DeviceHealthRemediationCandidate(
                    DeviceHealthRemediationAction.ReinstallOrRollbackDriver,
                    device.InstanceId,
                    deviceName,
                    device.ProblemCode,
                    "Reinstall or roll back the device driver",
                    "Driver replacement can affect hardware availability and requires explicit user approval plus device-specific evidence.",
                    AutomaticEligible: false,
                    Destructive: true));
            }

            return new DeviceHealthRemediationPlan(
                assessment,
                true,
                candidates,
                $"Sentinel verified {assessment.Problems.Count} device problem(s). Safe remediation guidance is available, but automatic driver replacement remains blocked.");
        }
    }

    public sealed record DeviceHealthRemediationPlan(
        DeviceHealthAssessment Assessment,
        bool ActionWarranted,
        IReadOnlyList<DeviceHealthRemediationCandidate> Candidates,
        string Summary)
    {
        public static DeviceHealthRemediationPlan NoAction(DeviceHealthAssessment assessment, string summary) =>
            new(assessment, false, Array.Empty<DeviceHealthRemediationCandidate>(), summary);
    }

    public sealed record DeviceHealthRemediationCandidate(
        DeviceHealthRemediationAction Action,
        string InstanceId,
        string DeviceName,
        int ProblemCode,
        string Title,
        string Evidence,
        bool AutomaticEligible,
        bool Destructive);

    public enum DeviceHealthRemediationAction
    {
        None,
        RecommendWindowsUpdateDriverCheck,
        ReinstallOrRollbackDriver
    }
}
