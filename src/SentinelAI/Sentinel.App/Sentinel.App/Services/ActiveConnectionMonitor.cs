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
    /// Collects active TCP connection ownership evidence without changing the system.
    /// Connections are treated as evidence only; the Investigation Engine decides
    /// whether multiple signals justify notifying the user.
    /// </summary>
    public sealed class ActiveConnectionMonitor
    {
        public ActiveConnectionSnapshot GetSnapshot()
        {
            List<ConnectionFinding> findings = new();
            int establishedCount = 0;
            int externalCount = 0;

            try
            {
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netstat.exe",
                        Arguments = "-ano -p tcp",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10000);

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    return ActiveConnectionSnapshot.Unavailable;
                }

                string[] lines = output.Split(
                    new[] { "\r\n", "\n" },
                    StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    string[] columns = line.Split(
                        (char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries);

                    if (columns.Length < 5 ||
                        !columns[0].Equals("TCP", StringComparison.OrdinalIgnoreCase) ||
                        !columns[3].Equals("ESTABLISHED", StringComparison.OrdinalIgnoreCase))
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

                    if (!int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
                    {
                        continue;
                    }

                    string processName = GetProcessName(processId);
                    ConnectionFinding? finding = Assess(processName, remoteAddress, remotePort, processId);
                    if (finding is not null)
                    {
                        findings.Add(finding);
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
                primary?.Reason ?? "No unusual active TCP connections were detected.");
        }

        private static ConnectionFinding? Assess(
            string processName,
            IPAddress remoteAddress,
            int remotePort,
            int processId)
        {
            bool uncommonRemotePort = remotePort is not (80 or 443 or 53 or 123 or 5228 or 8080 or 8443);
            bool systemProcess =
                processName.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("svchost", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("services", StringComparison.OrdinalIgnoreCase);

            if (!uncommonRemotePort || systemProcess)
            {
                return null;
            }

            string endpoint = $"{remoteAddress}:{remotePort}";
            return new ConnectionFinding(
                processName,
                endpoint,
                $"Uses an uncommon external TCP port ({remotePort}); process ID {processId}. Correlation is required before action.");
        }

        private static bool TryParseEndpoint(
            string value,
            out IPAddress? address,
            out int port)
        {
            address = null;
            port = 0;

            int separator = value.LastIndexOf(':');
            if (separator <= 0 || separator >= value.Length - 1)
            {
                return false;
            }

            string addressText = value[..separator].Trim('[', ']');
            string portText = value[(separator + 1)..];

            return IPAddress.TryParse(addressText, out address) &&
                   int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
        }

        private static bool IsLocalOrPrivate(IPAddress address)
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            {
                return true;
            }

            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                return bytes[0] == 10 ||
                       bytes[0] == 127 ||
                       (bytes[0] == 169 && bytes[1] == 254) ||
                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168);
            }

            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        }

        private static string GetProcessName(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                return process.ProcessName;
            }
            catch
            {
                return "Unknown process";
            }
        }

        private sealed record ConnectionFinding(
            string ProcessName,
            string RemoteEndpoint,
            string Reason);

        public sealed record ActiveConnectionSnapshot(
            int EstablishedConnectionCount,
            int ExternalConnectionCount,
            int ReviewConnectionCount,
            string PrimaryProcessName,
            string PrimaryRemoteEndpoint,
            string PrimaryReason)
        {
            public static ActiveConnectionSnapshot Unavailable { get; } =
                new(0, 0, 0, "Unavailable", "Unavailable", "Active connection evidence could not be collected.");
        }
    }
}
