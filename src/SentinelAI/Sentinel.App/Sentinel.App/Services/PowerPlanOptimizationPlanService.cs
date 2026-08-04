/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Converts verified power-plan evidence into a conservative optimization plan.
    /// Sentinel considers changing Power saver only while AC power is present; it
    /// never forces a laptop into a performance plan while running on battery.
    /// </summary>
    public sealed class PowerPlanOptimizationPlanService
    {
        private const string BalancedPlanGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private readonly PowerPlanHealthAssessmentService _assessmentService = new();

        public PowerPlanOptimizationPlan BuildPlan()
        {
            PowerPlanHealthAssessment assessment = _assessmentService.Assess();
            PowerSourceEvidence powerSource = GetPowerSourceEvidence();

            if (!assessment.OptimizationInvestigationWarranted)
            {
                return PowerPlanOptimizationPlan.NoAction(
                    assessment,
                    powerSource,
                    assessment.Summary);
            }

            if (!powerSource.Verified)
            {
                return PowerPlanOptimizationPlan.NoAction(
                    assessment,
                    powerSource,
                    "Sentinel found a potentially performance-limiting power plan but could not verify the current power source. No automatic change is warranted.");
            }

            if (!powerSource.OnAcPower)
            {
                return PowerPlanOptimizationPlan.NoAction(
                    assessment,
                    powerSource,
                    "Windows is using Power saver while the computer is on battery. Sentinel will preserve the user's battery-saving configuration.");
            }

            var candidates = new List<PowerPlanOptimizationCandidate>
            {
                new(
                    PowerPlanOptimizationAction.SwitchToBalanced,
                    BalancedPlanGuid,
                    "Switch to Balanced power plan",
                    "Windows is using Power saver while AC power is connected. Balanced removes unnecessary performance limiting while retaining normal Windows power management.",
                    AutomaticEligible: true,
                    Reversible: true)
            };

            return new PowerPlanOptimizationPlan(
                assessment,
                powerSource,
                true,
                candidates,
                "Sentinel verified Power saver is active while AC power is connected. A reversible switch to Balanced is eligible for final safety review.");
        }

        private static PowerSourceEvidence GetPowerSourceEvidence()
        {
            try
            {
                if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                    return PowerSourceEvidence.Unknown("Windows did not return power-source status.");

                bool onAc = status.ACLineStatus == 1;
                bool onBattery = status.ACLineStatus == 0;
                bool batteryPresent = status.BatteryFlag != 128 && status.BatteryFlag != 255;

                return new PowerSourceEvidence(
                    true,
                    onAc,
                    onBattery,
                    batteryPresent,
                    onAc
                        ? "AC power is connected."
                        : onBattery
                            ? "The computer is running on battery power."
                            : "Windows reported an unknown power-source state.");
            }
            catch (Exception ex)
            {
                return PowerSourceEvidence.Unknown(ex.Message);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }
    }

    public sealed record PowerPlanOptimizationPlan(
        PowerPlanHealthAssessment Assessment,
        PowerSourceEvidence PowerSource,
        bool ActionWarranted,
        IReadOnlyList<PowerPlanOptimizationCandidate> Candidates,
        string Summary)
    {
        public static PowerPlanOptimizationPlan NoAction(
            PowerPlanHealthAssessment assessment,
            PowerSourceEvidence powerSource,
            string summary) =>
            new(assessment, powerSource, false, Array.Empty<PowerPlanOptimizationCandidate>(), summary);
    }

    public sealed record PowerPlanOptimizationCandidate(
        PowerPlanOptimizationAction Action,
        string TargetPlanGuid,
        string Title,
        string Evidence,
        bool AutomaticEligible,
        bool Reversible);

    public sealed record PowerSourceEvidence(
        bool Verified,
        bool OnAcPower,
        bool OnBatteryPower,
        bool BatteryPresent,
        string Summary)
    {
        public static PowerSourceEvidence Unknown(string summary) =>
            new(false, false, false, false, summary);
    }

    public enum PowerPlanOptimizationAction
    {
        None,
        SwitchToBalanced
    }
}
