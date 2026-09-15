/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;

namespace Sentinel.App.Services
{
    public static class NetworkThroughputPolicy
    {
        private const double BitsPerMegabit = 1_000_000d;

        public static NetworkThroughputCalculation Calculate(
            IReadOnlyDictionary<string, NetworkCounterSample> previous,
            IReadOnlyDictionary<string, NetworkCounterSample> current,
            long timestampFrequency)
        {
            ArgumentNullException.ThrowIfNull(previous);
            ArgumentNullException.ThrowIfNull(current);
            if (timestampFrequency <= 0)
                return new NetworkThroughputCalculation(0, 0, current.Count > 0, 0);

            double downloadMbps = 0;
            double uploadMbps = 0;
            int contributingAdapters = 0;

            foreach ((string adapterId, NetworkCounterSample sample) in current)
            {
                if (!previous.TryGetValue(adapterId, out NetworkCounterSample prior))
                    continue;

                long timestampDelta = sample.Timestamp - prior.Timestamp;
                long receivedDelta = sample.BytesReceived - prior.BytesReceived;
                long sentDelta = sample.BytesSent - prior.BytesSent;
                if (timestampDelta <= 0 || receivedDelta < 0 || sentDelta < 0)
                    continue;

                double elapsedSeconds = timestampDelta / (double)timestampFrequency;
                if (elapsedSeconds <= 0 || double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds))
                    continue;

                downloadMbps += receivedDelta * 8d / elapsedSeconds / BitsPerMegabit;
                uploadMbps += sentDelta * 8d / elapsedSeconds / BitsPerMegabit;
                contributingAdapters++;
            }

            return new NetworkThroughputCalculation(
                Math.Max(downloadMbps, 0),
                Math.Max(uploadMbps, 0),
                current.Count > 0,
                contributingAdapters);
        }
    }

    public readonly record struct NetworkCounterSample(long BytesReceived, long BytesSent, long Timestamp);
    public readonly record struct NetworkThroughputCalculation(double DownloadMbps, double UploadMbps, bool IsConnected, int ContributingAdapters);
}
