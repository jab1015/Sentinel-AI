/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Service restart is disabled until it is implemented through Sentinel's
    /// privileged broker with explicit dependency impact and recoverable stop/start
    /// state. The previous direct ServiceController path could stop dependencies and
    /// leave the service stopped after cancellation or start failure.
    /// </summary>
    public sealed class ServiceRemediationService
    {
        private readonly RemediationPolicy _policy;

        public ServiceRemediationService(RemediationPolicy? policy = null)
        {
            _policy = policy ?? new RemediationPolicy();
        }

        public Task<ServiceRemediationResult> RestartAsync(
            string serviceName,
            bool hasVerifiedEvidence,
            bool isWindowsProtectedComponent,
            bool userApproved,
            bool canRequestElevation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(serviceName))
                return Task.FromResult(Failed("Sentinel could not verify the service identity."));

            var decision = _policy.Evaluate(new RemediationPolicy.RemediationRequest(
                RemediationPolicy.RemediationAction.RestartService,
                RemediationPolicy.RemediationRisk.Moderate,
                hasVerifiedEvidence,
                isWindowsProtectedComponent,
                RequiresElevation: true,
                CanRequestElevation: canRequestElevation));

            if (!decision.Allowed)
                return Task.FromResult(Failed(decision.Explanation));

            if (decision.RequiresUserApproval && !userApproved)
            {
                return Task.FromResult(new ServiceRemediationResult(
                    Succeeded: false,
                    RequiresUserApproval: true,
                    ServiceRunning: false,
                    Message: decision.Explanation));
            }

            return Task.FromResult(Failed(
                "Sentinel did not restart the service. Automatic service restart is temporarily disabled until dependency impact, elevation, rollback, and final running state are handled by the privileged broker and pass Windows runtime validation."));
        }

        public async Task<bool> IsRunningAsync(
            string serviceName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(serviceName)) return false;

            try
            {
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var service = new ServiceController(serviceName);
                    service.Refresh();
                    return service.Status == ServiceControllerStatus.Running;
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        private static ServiceRemediationResult Failed(string message) =>
            new(false, false, false, message);

        public sealed record ServiceRemediationResult(
            bool Succeeded,
            bool RequiresUserApproval,
            bool ServiceRunning,
            string Message);
    }
}
