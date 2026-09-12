/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;

namespace Sentinel.App.Services
{
    public sealed class NetworkMonitor
    {
        private readonly object _sampleLock = new();
        private readonly Dictionary<string, NetworkCounterSample> _previousSamples = new(StringComparer.OrdinalIgnoreCase);

        public bool IsConnected() => NetworkInterface.GetIsNetworkAvailable();

        public int GetActiveAdapterCount() => GetActiveAdapters().Length;

        public string GetPrimaryAdapterName()
        {
            NetworkInterface? adapter = GetActiveAdapters().OrderByDescending(n => n.Speed).FirstOrDefault();
            return adapter?.Name ?? "None";
        }

        public long GetSpeedMbps()
        {
            NetworkInterface? adapter = GetActiveAdapters().OrderByDescending(n => n.Speed).FirstOrDefault();
            return adapter == null ? 0 : adapter.Speed / 1_000_000;
        }

        public NetworkThroughputSnapshot GetThroughput()
        {
            try
            {
                NetworkInterface[] adapters = GetActiveAdapters();
                long now = Stopwatch.GetTimestamp();
                Dictionary<string, NetworkCounterSample> current = new(StringComparer.OrdinalIgnoreCase);

                foreach (NetworkInterface adapter in adapters)
                {
                    IPv4InterfaceStatistics statistics = adapter.GetIPv4Statistics();
                    current[adapter.Id] = new NetworkCounterSample(
                        statistics.BytesReceived,
                        statistics.BytesSent,
                        now);
                }

                lock (_sampleLock)
                {
                    NetworkThroughputCalculation calculation = NetworkThroughputPolicy.Calculate(
                        _previousSamples,
                        current,
                        Stopwatch.Frequency);

                    _previousSamples.Clear();
                    foreach ((string adapterId, NetworkCounterSample sample) in current)
                        _previousSamples[adapterId] = sample;

                    return new NetworkThroughputSnapshot(
                        calculation.DownloadMbps,
                        calculation.UploadMbps,
                        calculation.IsConnected);
                }
            }
            catch
            {
                return default;
            }
        }

        private static NetworkInterface[] GetActiveAdapters()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && ShouldContribute(n))
                .ToArray();
        }

        private static bool ShouldContribute(NetworkInterface adapter)
        {
            // Current activity is intentionally based on host-facing physical/PPP links.
            // Loopback, tunnels and obvious virtual/VPN adapters are excluded so mirrored
            // traffic is not counted twice. This is throughput activity, not link capacity.
            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                return false;

            string descriptor = (adapter.Description + " " + adapter.Name).ToLowerInvariant();
            if (descriptor.Contains("virtual") || descriptor.Contains("hyper-v") ||
                descriptor.Contains("vmware") || descriptor.Contains("vpn") ||
                descriptor.Contains("wireguard") || descriptor.Contains("tap-") ||
                descriptor.Contains("tun "))
                return false;

            return true;
        }

        public readonly record struct NetworkThroughputSnapshot(
            double DownloadMbps,
            double UploadMbps,
            bool IsConnected);
    }
}
