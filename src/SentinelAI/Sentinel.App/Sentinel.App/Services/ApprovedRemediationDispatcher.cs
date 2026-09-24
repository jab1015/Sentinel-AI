/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Routes an already-created and already-validated exact remediation request
    /// to the dedicated coordinator for that action. Unknown actions fail closed.
    /// </summary>
    public sealed class ApprovedRemediationDispatcher
    {
        private readonly ApprovedServiceRestartCoordinator _serviceRestart;
        private readonly ApprovedFirewallContainmentCoordinator _firewallContainment;
        private readonly ApprovedProcessContainmentCoordinator _processContainment;
        private readonly ApprovedQuarantineCoordinator _quarantine;

        public ApprovedRemediationDispatcher(
            ApprovedServiceRestartCoordinator? serviceRestart = null,
            ApprovedFirewallContainmentCoordinator? firewallContainment = null,
            ApprovedProcessContainmentCoordinator? processContainment = null,
            ApprovedQuarantineCoordinator? quarantine = null)
        {
            _serviceRestart = serviceRestart ?? new ApprovedServiceRestartCoordinator();
            _firewallContainment = firewallContainment ?? new ApprovedFirewallContainmentCoordinator();
            _processContainment = processContainment ?? new ApprovedProcessContainmentCoordinator();
            _quarantine = quarantine ?? new ApprovedQuarantineCoordinator();
        }

        public Task<ApprovedRemediationExecutor.ApprovedRemediationResult> ExecuteAsync(
            SystemSnapshot currentSnapshot,
            RemediationApprovalCoordinator.RemediationApprovalRequest request,
            RemediationApprovalCoordinator.ApprovalValidationResult validation,
            bool canRequestElevation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(currentSnapshot);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(validation);

            return ApprovedRemediationRoutingPolicy.Resolve(request.Action) switch
            {
                ApprovedRemediationRoute.ServiceRestart => _serviceRestart.ExecuteAsync(
                    currentSnapshot, request, validation, canRequestElevation, cancellationToken),

                ApprovedRemediationRoute.FirewallContainment => _firewallContainment.ExecuteAsync(
                    currentSnapshot, request, validation),

                ApprovedRemediationRoute.ProcessContainment => _processContainment.ExecuteAsync(
                    currentSnapshot, request, validation),

                ApprovedRemediationRoute.Quarantine => _quarantine.ExecuteAsync(
                    currentSnapshot, request, validation, cancellationToken),

                _ => Task.FromResult(
                    ApprovedRemediationExecutor.ApprovedRemediationResult.NotAttempted(
                        $"Unsupported approved remediation action '{request.Action}'. Sentinel made no system change."))
            };
        }
    }
}
