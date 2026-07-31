/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Final fail-safe validation for autonomous protection decisions. This is a
    /// runtime regression guard: contradictory or incomplete decisions are forced
    /// back to observation-only before any execution path can see them.
    /// </summary>
    public sealed class ProtectionDecisionSafetyValidator
    {
        public ValidationResult Validate(
            AutonomousProtectionCoordinator.AutonomousProtectionDecision decision)
        {
            ArgumentNullException.ThrowIfNull(decision);

            if (decision.CanExecuteAutomatically && decision.RequiresUserApproval)
            {
                return ValidationResult.Fail(
                    "A remediation decision cannot both execute automatically and require user approval.");
            }

            bool hasAction = !string.IsNullOrWhiteSpace(decision.Action) &&
                             !decision.Action.Equals("None", StringComparison.OrdinalIgnoreCase);
            bool hasTarget = !string.IsNullOrWhiteSpace(decision.Target) &&
                             !decision.Target.Equals("None", StringComparison.OrdinalIgnoreCase);

            if ((decision.CanExecuteAutomatically || decision.RequiresUserApproval) && (!hasAction || !hasTarget))
            {
                return ValidationResult.Fail(
                    "A remediation decision that can change the system must identify an exact action and target.");
            }

            if (!decision.CanExecuteAutomatically && !decision.RequiresUserApproval && (hasAction || hasTarget))
            {
                return ValidationResult.Fail(
                    "Observation-only decisions must not carry an executable remediation target.");
            }

            return ValidationResult.Pass();
        }

        public sealed record ValidationResult(bool IsValid, string Message)
        {
            public static ValidationResult Pass() => new(true, "Protection decision passed safety validation.");
            public static ValidationResult Fail(string message) => new(false, message);
        }
    }
}
