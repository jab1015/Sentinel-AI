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
    /// Performs an approved service restart only through Sentinel's authenticated
    /// privileged broker. The broker owns the allowlist, dependency check, durable
    /// rollback reservation, bounded stop/start sequence, crash recovery, and its
    /// own final-state verification. The desktop then independently verifies Running
    /// before any success result is returned to the user.
    /// </summary>
    public sealed class ServiceRemediationService
    {
        private readonly RemediationPolicy _policy;
        private readonly PrivilegedBrokerClient _broker;

        public ServiceRemediationService(RemediationPolicy? policy = null)
        {
            _policy = policy ?? new RemediationPolicy();
            _broker = new PrivilegedBrokerClient();
        }

        public async Task<ServiceRemediationResult> RestartAsync(
            string serviceName,
            bool hasVerifiedEvidence,
            bool isWindowsProtectedComponent,
            bool userApproved,
            bool canRequestElevation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(serviceName))
                return Failed("Sentinel could not verify the service identity.");

            string exactServiceName = serviceName.Trim();
            var decision = _policy.Evaluate(new RemediationPolicy.RemediationRequest(
                RemediationPolicy.RemediationAction.RestartService,
                RemediationPolicy.RemediationRisk.Moderate,
                hasVerifiedEvidence,
                isWindowsProtectedComponent,
                RequiresElevation: true,
                CanRequestElevation: canRequestElevation));

            if (!decision.Allowed)
                return Failed(decision.Explanation);

            if (decision.RequiresUserApproval && !userApproved)
            {
                return new ServiceRemediationResult(
                    Succeeded: false,
                    RequiresUserApproval: true,
                    ServiceRunning: false,
                    Message: decision.Explanation);
            }

            if (!_broker.IsBrokerPresent)
                return Failed("Sentinel's privileged broker is unavailable. No service state was changed.");

            BrokerInvocationResult brokerResult = await _broker
                .RestartServiceAsync(exactServiceName, cancellationToken)
                .ConfigureAwait(false);
            if (!brokerResult.Succeeded)
                return Failed(string.IsNullOrWhiteSpace(brokerResult.Message)
                    ? "The privileged broker did not verify a safe service restart."
                    : brokerResult.Message);

            bool running = await IsRunningAsync(exactServiceName, cancellationToken).ConfigureAwait(false);
            if (!running)
            {
                return Failed(
                    "The privileged broker completed the restart workflow, but the desktop verification could not confirm the service is Running. Sentinel will not report success.");
            }

            return new ServiceRemediationResult(
                Succeeded: true,
                RequiresUserApproval: false,
                ServiceRunning: true,
                Message: $"Sentinel restarted {exactServiceName} through the privileged broker and independently verified that the service is Running.");
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
                    using var service = new ServiceController(serviceName.Trim());
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
