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
            Require(file.Contains("File quarantine, restore, and the Protection Center", StringComparison.OrdinalIgnoreCase) &&
                    file.Contains("requires your approval", StringComparison.OrdinalIgnoreCase) &&
                    file.Contains("Restore and Delete Permanently are manual", StringComparison.OrdinalIgnoreCase),
                "File quarantine/restore intent did not preserve approval-gated quarantine and manual recovery semantics.");

            string quarantineUi = responder.Answer(
                "What is the quarantine button for if I can't manually quarantine or restore things?",
                snapshot);
            Require(quarantineUi.Contains("Protection Center", StringComparison.OrdinalIgnoreCase) &&
                    quarantineUi.Contains("not a file picker", StringComparison.OrdinalIgnoreCase) &&
                    quarantineUi.Contains("Restore and Delete Permanently are manual", StringComparison.OrdinalIgnoreCase),
                "Quarantine UI explanation did not describe the actual management/recovery behavior.");

            snapshot.ConnectionIntelligenceConfidenceScore = 20;
            snapshot.ConnectionIntelligenceHasCorroboratingEvidence = false;
            string flaggedNetwork = responder.Answer(
                "Can you quarantine the flagged condition?",
                snapshot);
            Require(flaggedNetwork.Contains("Network containment and rollback", StringComparison.OrdinalIgnoreCase) &&
                    flaggedNetwork.Contains("uncorroborated", StringComparison.OrdinalIgnoreCase) &&
                    flaggedNetwork.Contains("confidence 20%", StringComparison.OrdinalIgnoreCase),
                "Current flagged-network quarantine question did not explain why containment is being withheld.");

            string protectionStatus = responder.Answer(
                "What is Sentinel doing about the flagged conditions and when will it take action against them?",
                snapshot);
            Require(protectionStatus.Contains("Protection status", StringComparison.OrdinalIgnoreCase) &&
                    protectionStatus.Contains("20%", StringComparison.OrdinalIgnoreCase) &&
                    protectionStatus.Contains("80%", StringComparison.OrdinalIgnoreCase) &&
                    protectionStatus.Contains("No network containment is authorized", StringComparison.OrdinalIgnoreCase),
                "Ask Sentinel protection-status answer diverged from deterministic live protection policy.");

            snapshot.DefenderThreatEvidenceAvailable = true;
            snapshot.DefenderActiveThreatCount = 1;
            snapshot.DefenderFileQuarantineCandidateAvailable = true;
            snapshot.DefenderPrimaryThreatName = "Trojan:Win32/SentinelRegression";
            snapshot.DefenderPrimaryThreatFilePath = @"C:\Temp\sentinel-regression.exe";
            string defender = responder.Answer(
                "Does Defender see any malware or active threats?",
                snapshot);
            Require(defender.Contains("1 active threat", StringComparison.OrdinalIgnoreCase) &&
                    defender.Contains("SentinelRegression", StringComparison.OrdinalIgnoreCase) &&
                    defender.Contains("approval-gated", StringComparison.OrdinalIgnoreCase),
                "Ask Sentinel hid verified active Defender threat/quarantine evidence behind generic status text.");

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
