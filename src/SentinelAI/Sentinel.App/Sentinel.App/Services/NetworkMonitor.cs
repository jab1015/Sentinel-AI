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
        private const double BitsPerMegabit = 1_000_000d;
        private readonly object _sampleLock = new();
        private readonly Dictionary<string, AdapterSample> _previousSamples = new(StringComparer.OrdinalIgnoreCase);

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
                Dictionary<string, CurrentAdapterSample> current = new(StringComparer.OrdinalIgnoreCase);

                foreach (NetworkInterface adapter in adapters)
                {
                    IPv4InterfaceStatistics statistics = adapter.GetIPv4Statistics();
                    current[adapter.Id] = new CurrentAdapterSample(
                        statistics.BytesReceived,
                        statistics.BytesSent,
                        now);
                }

                lock (_sampleLock)
                {
                    long receivedDelta = 0;
                    long sentDelta = 0;
                    double longestElapsedSeconds = 0;
                    int contributingAdapters = 0;

                    foreach ((string adapterId, CurrentAdapterSample sample) in current)
                    {
                        if (_previousSamples.TryGetValue(adapterId, out AdapterSample? previous))
                        {
                            long timestampDelta = sample.Timestamp - previous.Timestamp;
                            long adapterReceivedDelta = sample.BytesReceived - previous.BytesReceived;
                            long adapterSentDelta = sample.BytesSent - previous.BytesSent;

                            // Counter reset/wrap or clock anomaly establishes a new baseline for
                            // this adapter only; it must not create a negative or positive spike.
                            if (timestampDelta > 0 && adapterReceivedDelta >= 0 && adapterSentDelta >= 0)
                            {
                                receivedDelta += adapterReceivedDelta;
                                sentDelta += adapterSentDelta;
                                longestElapsedSeconds = Math.Max(longestElapsedSeconds,
                                    timestampDelta / (double)Stopwatch.Frequency);
                                contributingAdapters++;
                            }
                        }
                    }

                    _previousSamples.Clear();
                    foreach ((string adapterId, CurrentAdapterSample sample) in current)
                        _previousSamples[adapterId] = new AdapterSample(sample.BytesReceived, sample.BytesSent, sample.Timestamp);

                    if (contributingAdapters == 0 || longestElapsedSeconds <= 0)
                        return new NetworkThroughputSnapshot(0, 0, adapters.Length > 0);

                    double downloadMbps = receivedDelta * 8d / longestElapsedSeconds / BitsPerMegabit;
                    double uploadMbps = sentDelta * 8d / longestElapsedSeconds / BitsPerMegabit;
                    return new NetworkThroughputSnapshot(
                        Math.Max(downloadMbps, 0),
                        Math.Max(uploadMbps, 0),
                        adapters.Length > 0);
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

        private sealed record AdapterSample(long BytesReceived, long BytesSent, long Timestamp);
        private readonly record struct CurrentAdapterSample(long BytesReceived, long BytesSent, long Timestamp);

        public readonly record struct NetworkThroughputSnapshot(
            double DownloadMbps,
            double UploadMbps,
            bool IsConnected);
    }
}
