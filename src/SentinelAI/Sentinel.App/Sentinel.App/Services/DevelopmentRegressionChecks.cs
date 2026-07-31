/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Fast deterministic regression checks for safety invariants that must never
    /// regress during development. These checks are executed in Debug builds only.
    /// </summary>
    public static class DevelopmentRegressionChecks
    {
        public static void Run()
        {
            VerifyGroundedHealthyAnswerPasses();
            VerifyUnsupportedSuccessfulActionIsBlocked();
            VerifyUnsupportedPerformedActionIsBlocked();
            VerifyUnsupportedThreatClaimIsBlocked();
        }

        private static void VerifyGroundedHealthyAnswerPasses()
        {
            var snapshot = HealthySnapshot();
            var response = Response("Sentinel's current verified evidence does not show a condition that requires your attention.");
            var result = new AskSentinelResponseSafetyValidator().Validate(response, snapshot);

            Require(result.IsSafe, "A grounded healthy-state response was incorrectly blocked.");
        }

        private static void VerifyUnsupportedSuccessfulActionIsBlocked()
        {
            var snapshot = HealthySnapshot();
            var response = Response("Sentinel successfully fixed the problem.");
            var result = new AskSentinelResponseSafetyValidator().Validate(response, snapshot);

            Require(!result.IsSafe, "An unsupported successful-remediation claim was not blocked.");
        }

        private static void VerifyUnsupportedPerformedActionIsBlocked()
        {
            var snapshot = HealthySnapshot();
            var response = Response("Sentinel quarantined the application.");
            var result = new AskSentinelResponseSafetyValidator().Validate(response, snapshot);

            Require(!result.IsSafe, "An unsupported performed-action claim was not blocked.");
        }

        private static void VerifyUnsupportedThreatClaimIsBlocked()
        {
            var snapshot = HealthySnapshot();
            var response = Response("Sentinel found malware on your computer.");
            var result = new AskSentinelResponseSafetyValidator().Validate(response, snapshot);

            Require(!result.IsSafe, "An unsupported threat claim was not blocked.");
        }

        private static SystemSnapshot HealthySnapshot() => new()
        {
            DefenderEnabled = true,
            FirewallEnabled = true,
            DefenderStatus = "Enabled",
            FirewallStatus = "Enabled",
            InvestigationRequiresAttention = false,
            InvestigationReasonCode = "Healthy",
            FlaggedProcessCount = 0,
            FlaggedConnectionCount = 0,
            FlaggedServiceCount = 0,
            RemediationAttempted = false,
            RemediationSucceeded = false,
            AutonomousProtectionAttempted = false,
            AutonomousProtectionSucceeded = false
        };

        private static AskSentinelResponseOrchestrator.AskSentinelResponse Response(string answer) =>
            new(
                Answer: answer,
                EvidenceTimestamp: DateTimeOffset.Now,
                EvidenceCount: 1,
                RequiresAttention: false,
                IsInsufficientEvidence: false,
                UsedInvestigationHistory: false,
                UsedRecommendationGuard: false,
                PassedFinalSafetyValidation: false,
                GroundingSummary: "Regression test evidence.");

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Sentinel regression check failed: {message}");
            }
        }
    }
}
