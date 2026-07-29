/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System.Linq;
using System.Net.NetworkInformation;

namespace Sentinel.App.Services
{
    public class NetworkMonitor
    {
        public bool IsConnected()
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }

        public int GetActiveAdapterCount()
        {
            return NetworkInterface
                .GetAllNetworkInterfaces()
                .Count(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
        }

        public string GetPrimaryAdapterName()
        {
            var adapter = NetworkInterface
                .GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            return adapter?.Name ?? "None";
        }

        public long GetSpeedMbps()
        {
            var adapter = NetworkInterface
                .GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            if (adapter == null)
                return 0;

            return adapter.Speed / 1_000_000;
        }
    }
}