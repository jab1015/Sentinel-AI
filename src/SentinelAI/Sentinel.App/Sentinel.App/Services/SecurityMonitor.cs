/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Microsoft.Win32;

namespace Sentinel.App.Services
{
    public class SecurityMonitor
    {
        public bool IsWindowsDefenderInstalled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows Defender");

                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public bool IsFirewallInstalled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\SharedAccess");

                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public string GetSecuritySummary()
        {
            return
                $"Defender: {(IsWindowsDefenderInstalled() ? "Installed" : "Not Detected")} | " +
                $"Firewall: {(IsFirewallInstalled() ? "Installed" : "Not Detected")}";
        }
    }
}