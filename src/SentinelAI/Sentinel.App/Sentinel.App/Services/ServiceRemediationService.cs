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
    /// Executes an explicitly approved restart of an exact Windows service.
    /// The service identity and current state are revalidated immediately before
    /// any system change, and the final running state is independently verified.
    /// </summary>
    public sealed class ServiceRemediationService
    {
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

        private readonly RemediationPolicy _policy;

        public ServiceRemediationService(RemediationPolicy? policy = null)
        {
            _policy = policy ?? new RemediationPolicy();
        }

        public async Task<ServiceRemediationResult> RestartAsync(
            string serviceName,
            bool hasVerifiedEvidence,
            bool isWindowsProtectedComponent,
            bool userApproved,
            bool canRequestElevation,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                return Failed("Sentinel could not verify the service identity.");
            }

            var decision = _policy.Evaluate(new RemediationPolicy.RemediationRequest(
                RemediationPolicy.RemediationAction.RestartService,
                RemediationPolicy.RemediationRisk.Moderate,
                hasVerifiedEvidence,
                isWindowsProtectedComponent,
                RequiresElevation: true,
                CanRequestElevation: canRequestElevation));

            if (!decision.Allowed)
            {
                return Failed(decision.Explanation);
            }

            if (decision.RequiresUserApproval && !userApproved)
            {
                return new ServiceRemediationResult(
                    Succeeded: false,
                    RequiresUserApproval: true,
                    ServiceRunning: false,
                    Message: decision.Explanation);
            }

            try
            {
                using var service = new ServiceController(serviceName);

                // Force Windows to resolve the exact service before changing state.
                _ = service.ServiceName;
                service.Refresh();
                cancellationToken.ThrowIfCancellationRequested();

                if (service.Status == ServiceControllerStatus.StopPending)
                {
                    await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, StopTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (service.Status != ServiceControllerStatus.Stopped)
                {
                    if (!service.CanStop)
                    {
                        return Failed("Windows reports that this service cannot be stopped safely, so Sentinel made no change.");
                    }

                    service.Stop();
                    await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, StopTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                service.Refresh();

                if (service.Status == ServiceControllerStatus.Stopped)
                {
                    service.Start();
                }

                await WaitForStatusAsync(service, ServiceControllerStatus.Running, StartTimeout, cancellationToken)
                    .ConfigureAwait(false);

                service.Refresh();
                bool running = service.Status == ServiceControllerStatus.Running;

                return new ServiceRemediationResult(
                    Succeeded: running,
                    RequiresUserApproval: false,
                    ServiceRunning: running,
                    Message: running
                        ? "Sentinel restarted the approved service and verified that it is running."
                        : "Sentinel attempted the approved restart but could not verify that the service returned to a running state.");
            }
            catch (OperationCanceledException)
            {
                return Failed("The service action was canceled before Sentinel could verify the result.");
            }
            catch (InvalidOperationException)
            {
                return Failed("Sentinel could not access the approved Windows service. No other system changes were made.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return Failed("Windows did not permit Sentinel to restart the approved service. No other system changes were made.");
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                return Failed("The approved service did not reach the expected state in time. Sentinel will continue investigating.");
            }
        }

        public async Task<bool> IsRunningAsync(
            string serviceName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                return false;
            }

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

        private static async Task WaitForStatusAsync(
            ServiceController service,
            ServiceControllerStatus desiredStatus,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                service.Refresh();

                if (service.Status == desiredStatus)
                {
                    return;
                }

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }

            service.Refresh();
            if (service.Status != desiredStatus)
            {
                throw new System.ServiceProcess.TimeoutException();
            }
        }

        private static ServiceRemediationResult Failed(string message) =>
            new(
                Succeeded: false,
                RequiresUserApproval: false,
                ServiceRunning: false,
                Message: message);

        public sealed record ServiceRemediationResult(
            bool Succeeded,
            bool RequiresUserApproval,
            bool ServiceRunning,
            string Message);
    }
}
