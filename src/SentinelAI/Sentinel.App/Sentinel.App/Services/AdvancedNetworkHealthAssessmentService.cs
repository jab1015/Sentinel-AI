/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Read-only advanced network health assessment. Sentinel verifies basic
    /// connectivity, DNS resolution, and Winsock catalog readability before any
    /// network repair is considered. This layer never resets network state.
    /// </summary>
    public sealed class AdvancedNetworkHealthAssessmentService
    {
        public AdvancedNetworkHealthAssessment Assess()
        {
            bool networkAvailable = NetworkInterface.GetIsNetworkAvailable();
            bool loopbackOk = TestLoopback();
            CommandResult winsock = Run("netsh.exe", "winsock", "show", "catalog");
            bool winsockReadable = winsock.ExitCode == 0 && !string.IsNullOrWhiteSpace(winsock.Output);

            CommandResult dns = Run("nslookup.exe", "www.microsoft.com");
            bool dnsWorking = dns.ExitCode == 0 &&
                !dns.Output.Contains("timed out", StringComparison.OrdinalIgnoreCase) &&
                !dns.Output.Contains("can't find", StringComparison.OrdinalIgnoreCase) &&
                !dns.Output.Contains("server failed", StringComparison.OrdinalIgnoreCase);

            bool repairInvestigationWarranted = networkAvailable && (!dnsWorking || !winsockReadable);

            string summary;
            if (!networkAvailable)
                summary = "Windows reports no active network connection. Sentinel will not reset network components from this evidence alone.";
            else if (!loopbackOk)
                summary = "Sentinel could not verify local TCP/IP loopback health. A deeper network-stack investigation is warranted before repair.";
            else if (!winsockReadable)
                summary = "Sentinel could not verify the Winsock catalog. A network-stack repair investigation is warranted before any reset.";
            else if (!dnsWorking)
                summary = "Sentinel verified network availability but DNS resolution failed. DNS-specific repair should be evaluated before any Winsock reset.";
            else
                summary = "Network availability, local TCP/IP, DNS resolution, and Winsock catalog health are verified.";

            return new AdvancedNetworkHealthAssessment(
                networkAvailable,
                loopbackOk,
                dnsWorking,
                winsockReadable,
                repairInvestigationWarranted,
                summary,
                dns.Output,
                dns.Error,
                winsock.Output,
                winsock.Error);
        }

        private static bool TestLoopback()
        {
            try
            {
                using var ping = new Ping();
                PingReply reply = ping.Send(System.Net.IPAddress.Loopback, 1000);
                return reply.Status == IPStatus.Success;
            }
            catch (PingException)
            {
                try
                {
                    using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    return true;
                }
                catch { return false; }
            }
            catch { return false; }
        }

        private static CommandResult Run(string fileName, params string[] arguments)
        {
            try
            {
                string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                ProcessStartInfo startInfo = new()
                {
                    FileName = string.IsNullOrWhiteSpace(system) ? fileName : Path.Combine(system, fileName),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (string argument in arguments)
                    startInfo.ArgumentList.Add(argument);

                ProcessExecutionResult execution = BoundedProcessRunner
                    .RunAsync(startInfo, TimeSpan.FromSeconds(5), maxOutputChars: 128_000)
                    .GetAwaiter()
                    .GetResult();

                return new CommandResult(
                    execution.Succeeded ? 0 : execution.ExitCode ?? -1,
                    execution.StandardOutput,
                    execution.StandardError.Length > 0 ? execution.StandardError : execution.Detail);
            }
            catch (Exception ex)
            {
                return new CommandResult(-1, string.Empty, ex.GetType().Name);
            }
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }

    public sealed record AdvancedNetworkHealthAssessment(
        bool NetworkAvailable,
        bool LoopbackHealthy,
        bool DnsResolutionWorking,
        bool WinsockCatalogReadable,
        bool RepairInvestigationWarranted,
        string Summary,
        string DnsDiagnosticOutput,
        string DnsDiagnosticError,
        string WinsockDiagnosticOutput,
        string WinsockDiagnosticError);
}
