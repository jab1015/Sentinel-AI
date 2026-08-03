/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Collects current TCP and UDP endpoint ownership evidence for Sentinel's continuous
    /// intrusion-monitoring pipeline. Collection is read-only; classification and response
    /// remain separate so ordinary network activity is never treated as a threat by itself.
    /// </summary>
    public sealed class ActiveConnectionMonitor
    {
        private const int NetstatTimeoutMilliseconds = 3000;

        public ActiveConnectionSnapshot GetSnapshot()
        {
            List<ConnectionFinding> findings = new();
            HashSet<LocalSocketKey> listeningSockets = new();
            int establishedCount = 0;
            int externalCount = 0;
            int listeningTcpCount = 0;
            int udpEndpointCount = 0;
            int attributedExternalCount = 0;
            int attributedUdpCount = 0;
            int inboundExternalCount = 0;
            int outboundExternalCount = 0;

            try
            {
                string[] lines = ReadNetstatLines();
                if (lines.Length == 0)
                {
                    return ActiveConnectionSnapshot.Unavailable;
                }

                // First pass: record listening sockets so established connections can be
                // direction-classified without guessing from port numbers alone.
                foreach (string line in lines)
                {
                    string[] columns = SplitColumns(line);
                    if (columns.Length < 5 ||
                        !columns[0].Equals("TCP", StringComparison.OrdinalIgnoreCase) ||
                        !columns[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase) ||
                        !int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) ||
                        !TryParseEndpoint(columns[1], out IPAddress? localAddress, out int localPort) ||
                        localAddress is null)
                    {
                        continue;
                    }

                    listeningTcpCount++;
                    listeningSockets.Add(new LocalSocketKey(localAddress, localPort, pid));
                }

                foreach (string line in lines)
                {
                    string[] columns = SplitColumns(line);
                    if (columns.Length < 4)
                    {
                        continue;
                    }

                    if (columns[0].Equals("TCP", StringComparison.OrdinalIgnoreCase))
                    {
                        if (columns.Length < 5 ||
                            !columns[3].Equals("ESTABLISHED", StringComparison.OrdinalIgnoreCase) ||
                            !int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) ||
                            !TryParseEndpoint(columns[1], out IPAddress? localAddress, out int localPort) ||
                            localAddress is null)
                        {
                            continue;
                        }

                        establishedCount++;
                        if (!TryParseEndpoint(columns[2], out IPAddress? remoteAddress, out int remotePort) ||
                            remoteAddress is null || IsLocalOrPrivate(remoteAddress))
                        {
                            continue;
                        }

                        externalCount++;
                        bool inbound = IsAcceptedInbound(listeningSockets, localAddress, localPort, pid);
                        if (inbound) inboundExternalCount++;
                        else outboundExternalCount++;

                        ProcessIdentity identity = GetProcessIdentity(pid);
                        if (!identity.ProcessName.Equals("Unknown process", StringComparison.OrdinalIgnoreCase))
                        {
                            attributedExternalCount++;
                        }

                        ConnectionFinding? finding = Assess(identity, remoteAddress, remotePort, inbound);
                        if (finding is not null)
                        {
                            findings.Add(finding);
                        }
                    }
                    else if (columns[0].Equals("UDP", StringComparison.OrdinalIgnoreCase))
                    {
                        if (columns.Length < 4 || !int.TryParse(columns[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int udpPid))
                        {
                            continue;
                        }

                        udpEndpointCount++;
                        ProcessIdentity identity = GetProcessIdentity(udpPid);
                        if (!identity.ProcessName.Equals("Unknown process", StringComparison.OrdinalIgnoreCase))
                        {
                            attributedUdpCount++;
                        }
                    }
                }
            }
            catch
            {
                return ActiveConnectionSnapshot.Unavailable;
            }

            ConnectionFinding? primary = findings.Count > 0 ? findings[0] : null;
            return new ActiveConnectionSnapshot(
                establishedCount,
                externalCount,
                findings.Count,
                primary?.ProcessName ?? "None",
                primary?.RemoteEndpoint ?? "None",
                primary?.Reason ?? "No unusual active TCP connections were detected.",
                listeningTcpCount,
                udpEndpointCount,
                attributedExternalCount,
                true,
                inboundExternalCount,
                outboundExternalCount,
                attributedUdpCount);
        }

        private static string[] ReadNetstatLines()
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netstat.exe",
                    Arguments = "-ano",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(NetstatTimeoutMilliseconds))
            {
                TryTerminate(process);
                return Array.Empty<string>();
            }

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<string>();
            }

            return output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string[] SplitColumns(string line) =>
            line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        private static bool IsAcceptedInbound(
            HashSet<LocalSocketKey> listeningSockets,
            IPAddress localAddress,
            int localPort,
            int processId)
        {
            if (listeningSockets.Contains(new LocalSocketKey(localAddress, localPort, processId)))
            {
                return true;
            }

            // A wildcard listener (0.0.0.0 / ::) can accept a connection on any local address.
            return listeningSockets.Contains(new LocalSocketKey(IPAddress.Any, localPort, processId)) ||
                   listeningSockets.Contains(new LocalSocketKey(IPAddress.IPv6Any, localPort, processId));
        }

        private static ConnectionFinding? Assess(
            ProcessIdentity identity,
            IPAddress remoteAddress,
            int remotePort,
            bool inbound)
        {
            bool uncommonRemotePort = remotePort is not (80 or 443 or 53 or 123 or 5228 or 8080 or 8443);
            bool systemProcess = identity.ProcessName.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                                 identity.ProcessName.Equals("svchost", StringComparison.OrdinalIgnoreCase) ||
                                 identity.ProcessName.Equals("services", StringComparison.OrdinalIgnoreCase);

            if (!uncommonRemotePort || systemProcess)
            {
                return null;
            }

            string endpoint = $"{remoteAddress}:{remotePort}";
            string executableContext = string.IsNullOrWhiteSpace(identity.ExecutablePath)
                ? "Executable path could not be read."
                : $"Executable: {ShortenPath(identity.ExecutablePath)}.";
            string direction = inbound ? "inbound" : "outbound";

            return new ConnectionFinding(
                identity.ProcessName,
                endpoint,
                $"{identity.ProcessName} (PID {identity.ProcessId}) owns an {direction} established connection involving {endpoint} on uncommon remote port {remotePort}. {executableContext} This is attribution evidence only; Sentinel requires correlation before recommending or blocking network activity.");
        }

        private static bool TryParseEndpoint(string value, out IPAddress? address, out int port)
        {
            address = null;
            port = 0;
            int separator = value.LastIndexOf(':');
            if (separator <= 0 || separator >= value.Length - 1) return false;
            string addressText = value[..separator].Trim('[', ']');
            string portText = value[(separator + 1)..];
            return IPAddress.TryParse(addressText, out address) &&
                   int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
        }

        private static bool IsLocalOrPrivate(IPAddress address)
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                return bytes[0] == 10 || bytes[0] == 127 ||
                       (bytes[0] == 169 && bytes[1] == 254) ||
                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168);
            }
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        }

        private static ProcessIdentity GetProcessIdentity(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                string path;
                try { path = process.MainModule?.FileName ?? string.Empty; }
                catch { path = string.Empty; }
                return new ProcessIdentity(processId, process.ProcessName, path);
            }
            catch { return new ProcessIdentity(processId, "Unknown process", string.Empty); }
        }

        private static void TryTerminate(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(500);
                }
            }
            catch { }
        }

        private static string ShortenPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string normalized = path.Replace('/', '\\');
            return normalized.Length <= 100 ? normalized : "..." + normalized[^97..];
        }

        private sealed record ProcessIdentity(int ProcessId, string ProcessName, string ExecutablePath);
        private sealed record ConnectionFinding(string ProcessName, string RemoteEndpoint, string Reason);
        private sealed record LocalSocketKey(IPAddress Address, int Port, int ProcessId);

        public sealed record ActiveConnectionSnapshot(
            int EstablishedConnectionCount,
            int ExternalConnectionCount,
            int ReviewConnectionCount,
            string PrimaryProcessName,
            string PrimaryRemoteEndpoint,
            string PrimaryReason,
            int ListeningTcpEndpointCount = 0,
            int UdpEndpointCount = 0,
            int AttributedExternalConnectionCount = 0,
            bool CollectionAvailable = false,
            int InboundExternalConnectionCount = 0,
            int OutboundExternalConnectionCount = 0,
            int AttributedUdpEndpointCount = 0)
        {
            public static ActiveConnectionSnapshot Unavailable { get; } =
                new(0, 0, 0, "Unavailable", "Unavailable", "Active connection evidence could not be collected.", 0, 0, 0, false, 0, 0, 0);
        }
    }
}
