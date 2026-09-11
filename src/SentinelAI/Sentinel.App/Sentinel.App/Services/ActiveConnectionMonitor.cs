/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Collects current TCP and UDP endpoint ownership evidence for Sentinel's continuous
    /// intrusion-monitoring pipeline and retains a bounded in-memory observation history.
    /// Collection is polling-based: it can miss connections that begin and end between samples,
    /// and netstat does not provide remote-peer attribution for connectionless UDP endpoints.
    /// Classification and response remain separate so ordinary network activity is never treated
    /// as a threat by itself.
    /// </summary>
    public sealed class ActiveConnectionMonitor
    {
        private const int NetstatTimeoutMilliseconds = 3000;
        private const int MaximumHistoryEntries = 2000;
        private static readonly TimeSpan HistoryRetention = TimeSpan.FromMinutes(10);
        private readonly Dictionary<ConnectionHistoryKey, ConnectionHistoryEntry> _history = new();

        public ActiveConnectionSnapshot GetSnapshot()
        {
            List<ConnectionFinding> findings = new();
            HashSet<LocalSocketKey> listeningSockets = new();
            int establishedCount = 0;
            int publicRemoteCount = 0;
            int privateRemoteCount = 0;
            int listeningTcpCount = 0;
            int udpEndpointCount = 0;
            int attributedTcpCount = 0;
            int attributedUdpCount = 0;
            int inboundRemoteCount = 0;
            int outboundRemoteCount = 0;
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;

            try
            {
                string[] lines = ReadNetstatLines();
                if (lines.Length == 0)
                    return ActiveConnectionSnapshot.Unavailable;

                PruneHistory(observedAt);

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
                    if (columns.Length < 4) continue;

                    if (columns[0].Equals("TCP", StringComparison.OrdinalIgnoreCase))
                    {
                        if (columns.Length < 5 ||
                            !columns[3].Equals("ESTABLISHED", StringComparison.OrdinalIgnoreCase) ||
                            !int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) ||
                            !TryParseEndpoint(columns[1], out IPAddress? localAddress, out int localPort) ||
                            localAddress is null ||
                            !TryParseEndpoint(columns[2], out IPAddress? remoteAddress, out int remotePort) ||
                            remoteAddress is null || IsNonRemoteAddress(remoteAddress))
                        {
                            continue;
                        }

                        establishedCount++;
                        bool privateRemote = IsPrivateOrLinkLocal(remoteAddress);
                        if (privateRemote) privateRemoteCount++;
                        else publicRemoteCount++;

                        bool inbound = IsAcceptedInbound(listeningSockets, localAddress, localPort, pid);
                        if (inbound) inboundRemoteCount++;
                        else outboundRemoteCount++;

                        ProcessIdentity identity = GetProcessIdentity(pid);
                        if (!identity.ProcessName.Equals("Unknown process", StringComparison.OrdinalIgnoreCase))
                            attributedTcpCount++;

                        // Retain every non-loopback established remote observation—including LAN/private
                        // destinations and common ports—for later correlation. Port number and process name
                        // are not trust boundaries and never authorize containment on their own.
                        RecordObservation(identity, remoteAddress, remotePort, inbound, privateRemote, observedAt);

                        ConnectionFinding? finding = Assess(identity, remoteAddress, remotePort, localPort, inbound, privateRemote);
                        if (finding is not null) findings.Add(finding);
                    }
                    else if (columns[0].Equals("UDP", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!int.TryParse(columns[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int udpPid))
                            continue;

                        udpEndpointCount++;
                        ProcessIdentity identity = GetProcessIdentity(udpPid);
                        if (!identity.ProcessName.Equals("Unknown process", StringComparison.OrdinalIgnoreCase))
                            attributedUdpCount++;
                    }
                }
            }
            catch
            {
                return ActiveConnectionSnapshot.Unavailable;
            }

            int recentUniqueConnections = 0;
            int repeatingConnections = 0;
            foreach (ConnectionHistoryEntry entry in _history.Values)
            {
                if (observedAt - entry.LastSeen > HistoryRetention) continue;
                recentUniqueConnections++;
                if (entry.ObservationCount >= 3) repeatingConnections++;
            }

            ConnectionFinding? primary = findings.Count > 0 ? findings[0] : null;
            return new ActiveConnectionSnapshot(
                establishedCount,
                publicRemoteCount,
                findings.Count,
                primary?.ProcessName ?? "None",
                primary?.RemoteEndpoint ?? "None",
                primary?.Reason ?? "No unusual active TCP connection evidence was detected in this polling sample.",
                listeningTcpCount,
                udpEndpointCount,
                attributedTcpCount,
                true,
                inboundRemoteCount,
                outboundRemoteCount,
                attributedUdpCount,
                recentUniqueConnections,
                repeatingConnections,
                privateRemoteCount,
                CollectionMode: "Polling",
                CanObserveShortLivedConnectionsReliably: false,
                CanAttributeUdpRemotePeers: false);
        }

        private void RecordObservation(
            ProcessIdentity identity,
            IPAddress remoteAddress,
            int remotePort,
            bool inbound,
            bool privateRemote,
            DateTimeOffset observedAt)
        {
            string processIdentity = string.IsNullOrWhiteSpace(identity.ExecutablePath)
                ? identity.ProcessName
                : identity.ExecutablePath;
            ConnectionHistoryKey key = new(
                identity.ProcessId,
                processIdentity,
                remoteAddress.ToString(),
                remotePort,
                inbound);

            if (_history.TryGetValue(key, out ConnectionHistoryEntry? existing))
            {
                existing.LastSeen = observedAt;
                existing.ObservationCount++;
                existing.ProcessName = identity.ProcessName;
                existing.ExecutablePath = identity.ExecutablePath;
                existing.PrivateOrLocalNetwork = privateRemote;
                return;
            }

            if (_history.Count >= MaximumHistoryEntries) RemoveOldestHistoryEntry();

            _history[key] = new ConnectionHistoryEntry
            {
                FirstSeen = observedAt,
                LastSeen = observedAt,
                ObservationCount = 1,
                ProcessName = identity.ProcessName,
                ExecutablePath = identity.ExecutablePath,
                PrivateOrLocalNetwork = privateRemote
            };
        }

        private void PruneHistory(DateTimeOffset now)
        {
            List<ConnectionHistoryKey>? staleKeys = null;
            foreach ((ConnectionHistoryKey key, ConnectionHistoryEntry entry) in _history)
            {
                if (now - entry.LastSeen <= HistoryRetention) continue;
                staleKeys ??= new List<ConnectionHistoryKey>();
                staleKeys.Add(key);
            }

            if (staleKeys is null) return;
            foreach (ConnectionHistoryKey key in staleKeys) _history.Remove(key);
        }

        private void RemoveOldestHistoryEntry()
        {
            ConnectionHistoryKey? oldestKey = null;
            DateTimeOffset oldestSeen = DateTimeOffset.MaxValue;
            foreach ((ConnectionHistoryKey key, ConnectionHistoryEntry entry) in _history)
            {
                if (entry.LastSeen >= oldestSeen) continue;
                oldestSeen = entry.LastSeen;
                oldestKey = key;
            }

            if (oldestKey is not null) _history.Remove(oldestKey);
        }

        private static string[] ReadNetstatLines()
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netstat.exe"),
                    Arguments = "-ano",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            Task<string> outputRead = process.StandardOutput.ReadToEndAsync();
            Task<string> errorRead = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(NetstatTimeoutMilliseconds))
            {
                TryTerminate(process);
                return Array.Empty<string>();
            }

            string output = outputRead.GetAwaiter().GetResult();
            _ = errorRead.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) return Array.Empty<string>();

            return output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string[] SplitColumns(string line) => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        private static bool IsAcceptedInbound(HashSet<LocalSocketKey> listeningSockets, IPAddress localAddress, int localPort, int processId)
        {
            if (listeningSockets.Contains(new LocalSocketKey(localAddress, localPort, processId))) return true;
            return listeningSockets.Contains(new LocalSocketKey(IPAddress.Any, localPort, processId)) ||
                   listeningSockets.Contains(new LocalSocketKey(IPAddress.IPv6Any, localPort, processId));
        }

        private static ConnectionFinding? Assess(
            ProcessIdentity identity,
            IPAddress remoteAddress,
            int remotePort,
            int localPort,
            bool inbound,
            bool privateRemote)
        {
            if (identity.ProcessName.Equals("Unknown process", StringComparison.OrdinalIgnoreCase)) return null;

            int assessedPort = inbound ? localPort : remotePort;
            bool uncommonPort = assessedPort is not (80 or 443 or 53 or 123 or 5228 or 8080 or 8443);

            // A common port is retained in telemetry/history but is not independently suspicious.
            // Likewise, System/svchost/services receive no name-based exemption; their observations
            // remain available to correlation, but this weak single signal does not create a finding.
            if (!uncommonPort) return null;

            string endpoint = $"{remoteAddress}:{remotePort}";
            string executableContext = string.IsNullOrWhiteSpace(identity.ExecutablePath)
                ? "Executable path could not be read."
                : $"Executable: {ShortenPath(identity.ExecutablePath)}.";
            string direction = inbound ? "inbound" : "outbound";
            string networkScope = privateRemote ? "private/local-network" : "public-network";
            string portContext = inbound ? $"local listening port {localPort}" : $"remote port {remotePort}";

            return new ConnectionFinding(identity.ProcessName, endpoint,
                $"{identity.ProcessName} (PID {identity.ProcessId}) owns an {direction} established {networkScope} connection involving {endpoint} on uncommon {portContext}. {executableContext} This is attribution evidence only; Sentinel requires additional correlated evidence before recommending or blocking network activity.");
        }

        private static bool TryParseEndpoint(string value, out IPAddress? address, out int port)
        {
            address = null;
            port = 0;
            int separator = value.LastIndexOf(':');
            if (separator <= 0 || separator >= value.Length - 1) return false;
            string addressText = value[..separator].Trim('[', ']');
            string portText = value[(separator + 1)..];
            return IPAddress.TryParse(addressText, out address) && int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
        }

        private static bool IsNonRemoteAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
        }

        private static bool IsPrivateOrLinkLocal(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                int first = bytes[0];
                int second = bytes[1];
                return first == 10 ||
                       (first == 100 && second >= 64 && second <= 127) ||
                       (first == 169 && second == 254) ||
                       (first == 172 && second >= 16 && second <= 31) ||
                       (first == 192 && second == 168);
            }

            bool uniqueLocal = bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC;
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || uniqueLocal;
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
        private sealed record ConnectionHistoryKey(int ProcessId, string ProcessIdentity, string RemoteAddress, int RemotePort, bool Inbound);

        private sealed class ConnectionHistoryEntry
        {
            public DateTimeOffset FirstSeen { get; set; }
            public DateTimeOffset LastSeen { get; set; }
            public int ObservationCount { get; set; }
            public string ProcessName { get; set; } = "Unknown process";
            public string ExecutablePath { get; set; } = string.Empty;
            public bool PrivateOrLocalNetwork { get; set; }
        }

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
            int AttributedUdpEndpointCount = 0,
            int RecentUniqueExternalConnectionCount = 0,
            int RepeatingExternalConnectionCount = 0,
            int PrivateOrLocalNetworkConnectionCount = 0,
            string CollectionMode = "Polling",
            bool CanObserveShortLivedConnectionsReliably = false,
            bool CanAttributeUdpRemotePeers = false)
        {
            public static ActiveConnectionSnapshot Unavailable { get; } =
                new(0, 0, 0, "Unavailable", "Unavailable", "Active connection evidence could not be collected.", 0, 0, 0, false);
        }
    }
}
