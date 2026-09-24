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
            VerifyRemediationIntentDoesNotCollapseToStatusSummary();
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

        private static void VerifyRemediationIntentDoesNotCollapseToStatusSummary()
        {
            SystemSnapshot snapshot = HealthySnapshot();
            snapshot.NetworkConnectionMonitoringAvailable = true;
            snapshot.NetworkConnectionMonitoringStatus = "Active";
            snapshot.EstablishedConnectionCount = 8;
            snapshot.ExternalConnectionCount = 8;
            snapshot.FlaggedConnectionCount = 1;
            snapshot.PrimaryFlaggedConnectionRemoteEndpoint = "203.0.113.10:443";
            snapshot.PrimaryFlaggedConnectionProcessName = "sample.exe";
            snapshot.PrimaryFlaggedConnectionReason = "Connection requires review.";

            AskSentinelLocalResponder responder = new();
            string network = responder.Answer(
                "If you quarantine the connection and it messes something up, can we restore it?",
                snapshot);

            Require(network.Contains("Network containment and rollback", StringComparison.OrdinalIgnoreCase),
                "Network quarantine/rollback intent collapsed to a generic network-status answer.");
            Require(network.Contains("automatically removes", StringComparison.OrdinalIgnoreCase) &&
                    network.Contains("remove the exact Sentinel-created firewall block", StringComparison.OrdinalIgnoreCase),
                "Network rollback answer did not preserve the supported automatic/manual reversal semantics.");

            string file = responder.Answer(
                "If you quarantine a file can I restore it later?",
                snapshot);
            Require(file.Contains("File quarantine and restore", StringComparison.OrdinalIgnoreCase),
                "File quarantine/restore intent did not use the reversible quarantine explanation.");

            string process = responder.Answer(
                "If you contain a process can you restore it?",
                snapshot);
            Require(process.Contains("not a reversible quarantine", StringComparison.OrdinalIgnoreCase),
                "Process containment answer incorrectly implied reversible restore semantics.");

            string service = responder.Answer(
                "If Sentinel restarts a service can it roll that back?",
                snapshot);
            Require(service.Contains("not a quarantine", StringComparison.OrdinalIgnoreCase),
                "Service remediation answer did not distinguish restart from reversible quarantine.");
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
