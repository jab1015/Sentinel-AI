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
        private const string DefenderAdvancedThreatProtection = @"SOFTWARE\Policies\Microsoft\Windows Advanced Threat Protection";
        private const string FirewallProfiles = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";

        public SecurityStatusSnapshot GetStatus()
        {
            return new SecurityStatusSnapshot(
                GetDefenderStatus(),
                GetFirewallStatus());
        }

        public bool IsWindowsDefenderInstalled()
        {
            string status = GetDefenderStatus();
            return status != "Not detected" && status != "Unavailable";
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

                using RegistryKey? advancedThreatProtectionKey =
                    Registry.LocalMachine.OpenSubKey(DefenderAdvancedThreatProtection);

                if (!TryReadOptionalDword(realTimeKey?.GetValue("DisableRealtimeMonitoring"), out int disableRealTimeMonitoring) ||
                    !TryReadOptionalDword(defenderKey.GetValue("DisableAntiSpyware"), out int disableAntiSpyware) ||
                    !TryReadOptionalDword(defenderKey.GetValue("PassiveMode"), out int passiveMode) ||
                    !TryReadOptionalDword(advancedThreatProtectionKey?.GetValue("ForceDefenderPassiveMode"), out int forcedPassiveMode))
                {
                    return "Unavailable";
                }

                bool disabledByPolicy = disableRealTimeMonitoring != 0 || disableAntiSpyware != 0;
                bool passiveByPolicy = passiveMode != 0 || forcedPassiveMode != 0;

                if (engineRunning && !disabledByPolicy && !passiveByPolicy)
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
                        unknownProfiles++;
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

                if (detectedProfiles == 0 || enabledProfiles + disabledProfiles == 0)
                {
                    return "Unavailable";
                }

                if (unknownProfiles == 0 && enabledProfiles == profileNames.Length)
                {
                    return "Enabled";
                }

                if (unknownProfiles == 0 && disabledProfiles == profileNames.Length)
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

        private static bool TryReadOptionalDword(object? value, out int converted)
        {
            if (value is null)
            {
                converted = 0;
                return true;
            }

            return TryConvertToInt32(value, out converted);
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
