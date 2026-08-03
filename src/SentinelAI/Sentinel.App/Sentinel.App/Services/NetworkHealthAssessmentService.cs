/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Read-only network health assessment used before Sentinel considers DNS,
    /// Winsock, or adapter repair. This service never resets networking.
    /// </summary>
    public sealed class NetworkHealthAssessmentService
    {
        public async Task<NetworkHealthAssessment> AssessAsync(
            CancellationToken cancellationToken = default)
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            NetworkInterface[] active = interfaces
                .Where(adapter =>
                    adapter.OperationalStatus == OperationalStatus.Up &&
                    adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .ToArray();

            bool hasActiveAdapter = active.Length > 0;
            bool hasDefaultGateway = active.Any(adapter =>
            {
                try
                {
                    return adapter.GetIPProperties().GatewayAddresses
                        .Any(gateway => gateway.Address is not null);
                }
                catch
                {
                    return false;
                }
            });

            bool dnsConfigured = active.Any(adapter =>
            {
                try
                {
                    return adapter.GetIPProperties().DnsAddresses.Count > 0;
                }
                catch
                {
                    return false;
                }
            });

            bool gatewayReachable = false;
            string gatewayAddress = string.Empty;

            foreach (NetworkInterface adapter in active)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var gateway = adapter.GetIPProperties().GatewayAddresses
                        .Select(item => item.Address)
                        .FirstOrDefault(address => address is not null);

                    if (gateway is null)
                        continue;

                    gatewayAddress = gateway.ToString();
                    using Ping ping = new();

                    // Use the broadly supported timeout overload so this remains
                    // compatible with the Windows 10 target framework surface.
                    PingReply reply = await ping.SendPingAsync(gateway, 2000)
                        .ConfigureAwait(false);

                    cancellationToken.ThrowIfCancellationRequested();

                    if (reply.Status == IPStatus.Success)
                    {
                        gatewayReachable = true;
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Some gateways intentionally ignore ICMP. Lack of ping is evidence
                    // only and must not by itself trigger a network reset.
                }
            }

            CommandResult dnsResult = await RunAsync(
                "nslookup.exe",
                "www.microsoft.com",
                cancellationToken).ConfigureAwait(false);

            bool dnsResolutionSucceeded =
                dnsResult.ExitCode == 0 &&
                (dnsResult.Output.Contains("Address:", StringComparison.OrdinalIgnoreCase) ||
                 dnsResult.Output.Contains("Addresses:", StringComparison.OrdinalIgnoreCase));

            bool repairInvestigationWarranted =
                hasActiveAdapter &&
                hasDefaultGateway &&
                dnsConfigured &&
                !dnsResolutionSucceeded;

            string summary;
            if (!hasActiveAdapter)
                summary = "Sentinel could not verify an active network adapter. No automatic network repair is warranted from this evidence alone.";
            else if (!hasDefaultGateway)
                summary = "An active adapter is present, but Sentinel could not verify a default gateway. No automatic reset will be attempted.";
            else if (!dnsConfigured)
                summary = "An active network path exists, but Sentinel could not verify configured DNS servers. Further investigation is required before repair.";
            else if (dnsResolutionSucceeded)
                summary = "Network and DNS health checks passed. No network repair is warranted.";
            else
                summary = "The local network path appears present, but DNS resolution failed. Sentinel should verify persistence before considering a DNS repair.";

            return new NetworkHealthAssessment(
                active.Select(adapter => adapter.Name).ToArray(),
                hasActiveAdapter,
                hasDefaultGateway,
                gatewayAddress,
                gatewayReachable,
                dnsConfigured,
                dnsResolutionSucceeded,
                repairInvestigationWarranted,
                summary,
                dnsResult.Output,
                dnsResult.Error);
        }

        private static async Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            CancellationToken cancellationToken)
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new CommandResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record NetworkHealthAssessment(
        IReadOnlyList<string> ActiveAdapters,
        bool HasActiveAdapter,
        bool HasDefaultGateway,
        string GatewayAddress,
        bool GatewayReachable,
        bool DnsConfigured,
        bool DnsResolutionSucceeded,
        bool RepairInvestigationWarranted,
        string Summary,
        string DnsDiagnosticOutput,
        string DnsDiagnosticError);
}
