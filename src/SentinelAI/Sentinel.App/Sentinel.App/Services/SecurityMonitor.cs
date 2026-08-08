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
                int disabledProfiles = 0;
                int unknownProfiles = 0;

                foreach (string profileName in profileNames)
                {
                    using RegistryKey? profileKey = Registry.LocalMachine.OpenSubKey(
                        $@"{FirewallProfiles}\{profileName}");

                    if (profileKey is null)
                    {
                        continue;
                    }

                    detectedProfiles++;
                    object? rawValue = profileKey.GetValue("EnableFirewall");
                    if (!TryConvertToInt32(rawValue, out int enabled))
                    {
                        unknownProfiles++;
                    }
                    else if (enabled != 0)
                    {
                        enabledProfiles++;
                    }
                    else
                    {
                        disabledProfiles++;
                    }
                }

                if (detectedProfiles == 0 || unknownProfiles == detectedProfiles)
                {
                    return "Unavailable";
                }

                if (unknownProfiles == 0 && enabledProfiles == detectedProfiles)
                {
                    return "Enabled";
                }

                if (unknownProfiles == 0 && disabledProfiles == detectedProfiles)
                {
                    return "Disabled";
                }

                return $"Partial ({enabledProfiles} enabled, {disabledProfiles} disabled, {unknownProfiles} unknown)";
            }
            catch
            {
                return "Unavailable";
            }
        }

        private static bool TryConvertToInt32(object? value, out int converted)
        {
            try
            {
                if (value is null)
                {
                    converted = 0;
                    return false;
                }

                converted = Convert.ToInt32(value);
                return true;
            }
            catch
            {
                converted = 0;
                return false;
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
