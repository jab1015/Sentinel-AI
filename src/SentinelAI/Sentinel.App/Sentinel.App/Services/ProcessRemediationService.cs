/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Executes a user-approved process termination only after the central
    /// remediation policy has allowed the exact action.
    /// </summary>
    public sealed class ProcessRemediationService
    {
        private readonly RemediationPolicy _policy;

        public ProcessRemediationService(RemediationPolicy? policy = null)
        {
            _policy = policy ?? new RemediationPolicy();
        }

        public async Task<ProcessRemediationResult> TerminateAsync(
            int processId,
            string expectedProcessName,
            bool hasVerifiedEvidence,
            bool isWindowsProtectedComponent,
            bool userApproved,
            CancellationToken cancellationToken = default)
        {
            if (processId <= 0)
            {
                return Failed("Sentinel could not identify a valid process to close.");
            }

            if (string.IsNullOrWhiteSpace(expectedProcessName))
            {
                return Failed("Sentinel could not verify the process identity.");
            }

            var decision = _policy.Evaluate(new RemediationPolicy.RemediationRequest(
                RemediationPolicy.RemediationAction.TerminateProcess,
                RemediationPolicy.RemediationRisk.Moderate,
                hasVerifiedEvidence,
                isWindowsProtectedComponent,
                RequiresElevation: false,
                CanRequestElevation: false));

            if (!decision.Allowed)
            {
                return Failed(decision.Explanation);
            }

            if (decision.RequiresUserApproval && !userApproved)
            {
                return new ProcessRemediationResult(
                    Succeeded: false,
                    RequiresUserApproval: true,
                    ProcessExited: false,
                    Message: decision.Explanation);
            }

            try
            {
                using var process = Process.GetProcessById(processId);

                if (!string.Equals(
                        process.ProcessName,
                        expectedProcessName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Failed("The process changed before Sentinel could act, so no action was taken.");
                }

                if (process.HasExited)
                {
                    return new ProcessRemediationResult(
                        Succeeded: true,
                        RequiresUserApproval: false,
                        ProcessExited: true,
                        Message: "The process had already closed. No further action was required.");
                }

                process.Kill(entireProcessTree: false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                bool exited = process.HasExited;
                return new ProcessRemediationResult(
                    Succeeded: exited,
                    RequiresUserApproval: false,
                    ProcessExited: exited,
                    Message: exited
                        ? "Sentinel closed the approved process and verified that it stopped."
                        : "Sentinel requested that the process close, but could not verify that it stopped.");
            }
            catch (ArgumentException)
            {
                return new ProcessRemediationResult(
                    Succeeded: true,
                    RequiresUserApproval: false,
                    ProcessExited: true,
                    Message: "The process was no longer running. No further action was required.");
            }
            catch (OperationCanceledException)
            {
                return Failed("The process action was canceled before Sentinel could verify the result.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                return Failed("Sentinel could not safely close the process. No other system changes were made.");
            }
        }

        private static ProcessRemediationResult Failed(string message) =>
            new(
                Succeeded: false,
                RequiresUserApproval: false,
                ProcessExited: false,
                Message: message);

        public sealed record ProcessRemediationResult(
            bool Succeeded,
            bool RequiresUserApproval,
            bool ProcessExited,
            string Message);
    }
}
