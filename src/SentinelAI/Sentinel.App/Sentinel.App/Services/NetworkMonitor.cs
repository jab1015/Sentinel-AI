/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;

namespace Sentinel.App.Services
{
    public sealed class NetworkMonitor
    {
        private const double BitsPerMegabit = 1_000_000d;

        private readonly object _sampleLock = new();
        private long _previousBytesReceived;
        private long _previousBytesSent;
        private long _previousTimestamp;
        private bool _hasPreviousSample;

        public bool IsConnected()
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }

        public int GetActiveAdapterCount()
        {
            return GetActiveAdapters().Length;
        }

        public string GetPrimaryAdapterName()
        {
            NetworkInterface? adapter = GetActiveAdapters()
                .OrderByDescending(n => n.Speed)
                .FirstOrDefault();

            return adapter?.Name ?? "None";
        }

        public long GetSpeedMbps()
        {
            NetworkInterface? adapter = GetActiveAdapters()
                .OrderByDescending(n => n.Speed)
                .FirstOrDefault();

            return adapter == null ? 0 : adapter.Speed / 1_000_000;
        }

        public NetworkThroughputSnapshot GetThroughput()
        {
            try
            {
                NetworkInterface[] adapters = GetActiveAdapters();
                long currentBytesReceived = 0;
                long currentBytesSent = 0;

                foreach (NetworkInterface adapter in adapters)
                {
                    IPv4InterfaceStatistics statistics = adapter.GetIPv4Statistics();
                    currentBytesReceived += statistics.BytesReceived;
                    currentBytesSent += statistics.BytesSent;
                }

                long currentTimestamp = Stopwatch.GetTimestamp();

                lock (_sampleLock)
                {
                    if (!_hasPreviousSample)
                    {
                        StoreSample(currentBytesReceived, currentBytesSent, currentTimestamp);
                        _hasPreviousSample = true;
                        return new NetworkThroughputSnapshot(0, 0, adapters.Length > 0);
                    }

                    long receivedDelta = currentBytesReceived - _previousBytesReceived;
                    long sentDelta = currentBytesSent - _previousBytesSent;
                    long timestampDelta = currentTimestamp - _previousTimestamp;

                    StoreSample(currentBytesReceived, currentBytesSent, currentTimestamp);

                    if (receivedDelta < 0 || sentDelta < 0 || timestampDelta <= 0)
                    {
                        return new NetworkThroughputSnapshot(0, 0, adapters.Length > 0);
                    }

                    double elapsedSeconds = timestampDelta / (double)Stopwatch.Frequency;
                    double downloadMbps = receivedDelta * 8d / elapsedSeconds / BitsPerMegabit;
                    double uploadMbps = sentDelta * 8d / elapsedSeconds / BitsPerMegabit;

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
            return NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .ToArray();
        }

        private void StoreSample(long bytesReceived, long bytesSent, long timestamp)
        {
            _previousBytesReceived = bytesReceived;
            _previousBytesSent = bytesSent;
            _previousTimestamp = timestamp;
        }

        public readonly record struct NetworkThroughputSnapshot(
            double DownloadMbps,
            double UploadMbps,
            bool IsConnected);
    }
}
