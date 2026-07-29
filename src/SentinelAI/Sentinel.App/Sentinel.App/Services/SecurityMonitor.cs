/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Linq;

namespace Sentinel.App.Services
{
    public sealed class SecurityMonitor
    {
        private const string DefenderRoot = @"SOFTWARE\Microsoft\Windows Defender";
        private const string DefenderRealTimeProtection = DefenderRoot + @"\Real-Time Protection";
        private const string FirewallProfiles = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";

        public SecurityStatusSnapshot GetStatus()
        {
            return new SecurityStatusSnapshot(
                GetDefenderStatus(),
                GetFirewallStatus());
        }

        public bool IsWindowsDefenderInstalled()
        {
            return GetDefenderStatus() != "Not detected";
        }

        public bool IsFirewallInstalled()
        {
            return GetFirewallStatus() != "Unavailable";
        }

        public string GetSecuritySummary()
        {
            SecurityStatusSnapshot status = GetStatus();
            return $"Defender: {status.DefenderStatus} | Firewall: {status.FirewallStatus}";
        }

        private static string GetDefenderStatus()
        {
            try
            {
                using RegistryKey? defenderKey = Registry.LocalMachine.OpenSubKey(DefenderRoot);
                if (defenderKey is null)
                {
                    return "Not detected";
                }

                bool engineRunning = Process.GetProcessesByName("MsMpEng").Any();

                using RegistryKey? realTimeKey =
                    Registry.LocalMachine.OpenSubKey(DefenderRealTimeProtection);

                int disableRealTimeMonitoring = ConvertToInt32(
                    realTimeKey?.GetValue("DisableRealtimeMonitoring"),
                    defaultValue: 0);

                if (engineRunning && disableRealTimeMonitoring == 0)
                {
                    return "Enabled";
                }

                return engineRunning ? "Limited" : "Disabled or inactive";
            }
            catch
            {
                return "Unavailable";
            }
        }

        private static string GetFirewallStatus()
        {
            try
            {
                string[] profileNames =
                {
                    "DomainProfile",
                    "StandardProfile",
                    "PublicProfile"
                };

                int detectedProfiles = 0;
                int enabledProfiles = 0;

                foreach (string profileName in profileNames)
                {
                    using RegistryKey? profileKey = Registry.LocalMachine.OpenSubKey(
                        $@"{FirewallProfiles}\{profileName}");

                    if (profileKey is null)
                    {
                        continue;
                    }

                    detectedProfiles++;

                    int enabled = ConvertToInt32(
                        profileKey.GetValue("EnableFirewall"),
                        defaultValue: 1);

                    if (enabled != 0)
                    {
                        enabledProfiles++;
                    }
                }

                if (detectedProfiles == 0)
                {
                    return "Unavailable";
                }

                if (enabledProfiles == detectedProfiles)
                {
                    return "Enabled";
                }

                if (enabledProfiles == 0)
                {
                    return "Disabled";
                }

                return $"Partial ({enabledProfiles}/{detectedProfiles} profiles)";
            }
            catch
            {
                return "Unavailable";
            }
        }

        private static int ConvertToInt32(object? value, int defaultValue)
        {
            try
            {
                return value is null ? defaultValue : Convert.ToInt32(value);
            }
            catch
            {
                return defaultValue;
            }
        }

        public readonly record struct SecurityStatusSnapshot(
            string DefenderStatus,
            string FirewallStatus);
    }
}
