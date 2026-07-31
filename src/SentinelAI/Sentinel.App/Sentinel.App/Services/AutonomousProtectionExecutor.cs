/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Executes only the narrow low-risk actions that RemediationPolicy has
    /// explicitly approved for automatic use. Moderate/high-risk actions remain
    /// outside this executor and require their dedicated approval workflows.
    /// </summary>
    public sealed class AutonomousProtectionExecutor
    {
        public async Task<AutonomousProtectionExecutionResult> ExecuteAsync(
            SystemSnapshot snapshot,
            AutonomousProtectionCoordinator.AutonomousProtectionDecision decision,
            Func<Task> refreshSecurityStateAsync,
            Func<Task> retryTransientOperationAsync)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(decision);
            ArgumentNullException.ThrowIfNull(refreshSecurityStateAsync);
            ArgumentNullException.ThrowIfNull(retryTransientOperationAsync);

            if (!decision.CanExecuteAutomatically || decision.RequiresUserApproval)
            {
                return AutonomousProtectionExecutionResult.NotAttempted(
                    "Sentinel did not change the computer because this action is not approved for automatic execution.");
            }

            try
            {
                switch (decision.Action)
                {
                    case "refresh-security-state":
                        await refreshSecurityStateAsync().ConfigureAwait(false);
                        return AutonomousProtectionExecutionResult.Success(
                            "Security state refreshed",
                            "Sentinel refreshed the current Windows security state and will verify it during the next investigation pass.");

                    case "retry-transient-operation":
                        await retryTransientOperationAsync().ConfigureAwait(false);
                        return AutonomousProtectionExecutionResult.Success(
                            "Temporary operation retried",
                            "Sentinel safely retried the temporary operation and will verify whether the condition returns.");

                    default:
                        return AutonomousProtectionExecutionResult.NotAttempted(
                            "The requested action is not on Sentinel's automatic-execution allow list.");
                }
            }
            catch (Exception ex)
            {
                return AutonomousProtectionExecutionResult.Failure(
                    "Automatic protection could not complete",
                    $"Sentinel left the computer unchanged where possible and will continue monitoring. {ex.Message}");
            }
        }

        public sealed record AutonomousProtectionExecutionResult(
            bool Attempted,
            bool Succeeded,
            string Title,
            string Summary,
            DateTimeOffset? CompletedAt)
        {
            public static AutonomousProtectionExecutionResult NotAttempted(string summary) =>
                new(false, false, string.Empty, summary, null);

            public static AutonomousProtectionExecutionResult Success(string title, string summary) =>
                new(true, true, title, summary, DateTimeOffset.Now);

            public static AutonomousProtectionExecutionResult Failure(string title, string summary) =>
                new(true, false, title, summary, DateTimeOffset.Now);
        }
    }
}
