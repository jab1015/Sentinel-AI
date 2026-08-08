/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;

namespace Sentinel.App.Services
{
    public sealed class ServiceMonitor
    {
        private const string ServicesRegistryPath = @"SYSTEM\CurrentControlSet\Services";

        public ServiceIntelligenceSnapshot GetIntelligence()
        {
            int installedCount = 0;
            int runningCount = 0;
            int flaggedCount = 0;
            string primaryServiceName = "None";
            string primaryReason = "No service warning conditions were detected.";
            bool collectionComplete = true;

            ServiceController[] services;
            try
            {
                services = ServiceController.GetServices();
            }
            catch
            {
                return new ServiceIntelligenceSnapshot(
                    0,
                    0,
                    0,
                    "Unavailable",
                    "Windows service information could not be read.",
                    false);
            }

            try
            {
                installedCount = services.Length;

                foreach (ServiceController service in services)
                {
                    try
                    {
                        if (service.Status == ServiceControllerStatus.Running)
                        {
                            runningCount++;
                        }

                        ServiceRegistryInfo registryInfo = ReadRegistryInfo(service.ServiceName);
                        if (!registryInfo.Available) collectionComplete = false;
                        string? finding = EvaluateService(service, registryInfo);
                        if (finding is null)
                        {
                            continue;
                        }

                        flaggedCount++;
                        if (primaryServiceName == "None")
                        {
                            primaryServiceName = string.IsNullOrWhiteSpace(service.DisplayName)
                                ? service.ServiceName
                                : service.DisplayName;
                            primaryReason = finding;
                        }
                    }
                    catch
                    {
                        // Protected, deleted, and transient services are skipped safely.
                    }
                }
            }
            finally
            {
                foreach (ServiceController service in services)
                {
                    service.Dispose();
                }
            }

            return new ServiceIntelligenceSnapshot(
                installedCount,
                runningCount,
                flaggedCount,
                primaryServiceName,
                primaryReason,
                collectionComplete);
        }

        public ServiceStatusSnapshot GetServiceStatus(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return new ServiceStatusSnapshot(false, false, "Unknown", "No service name was available for verification.");
            }

            ServiceController[] services;
            try
            {
                services = ServiceController.GetServices();
            }
            catch
            {
                return new ServiceStatusSnapshot(false, false, "Unavailable", "Windows service information could not be read.");
            }

            try
            {
                foreach (ServiceController service in services)
                {
                    try
                    {
                        if (!string.Equals(service.DisplayName, displayName, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(service.ServiceName, displayName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        service.Refresh();
                        string status = service.Status.ToString();
                        bool running = service.Status == ServiceControllerStatus.Running;
                        return new ServiceStatusSnapshot(
                            true,
                            running,
                            status,
                            running
                                ? $"{displayName} is currently running."
                                : $"{displayName} is currently {status.ToLowerInvariant()}.");
                    }
                    catch
                    {
                        return new ServiceStatusSnapshot(true, false, "Unavailable", $"{displayName} was found, but its current status could not be read.");
                    }
                }
            }
            finally
            {
                foreach (ServiceController service in services)
                {
                    service.Dispose();
                }
            }

            return new ServiceStatusSnapshot(false, false, "Not found", $"Windows could not find a service named {displayName}.");
        }

        private static string? EvaluateService(
            ServiceController service,
            ServiceRegistryInfo registryInfo)
        {
            if (!string.IsNullOrWhiteSpace(registryInfo.ExecutablePath) &&
                IsTemporaryLocation(registryInfo.ExecutablePath))
            {
                return $"Service binary is running from a temporary location: {ShortenPath(registryInfo.ExecutablePath)}";
            }

            if (registryInfo.StartType == 2 &&
                !registryInfo.DelayedAutoStart &&
                service.Status == ServiceControllerStatus.Stopped &&
                !string.IsNullOrWhiteSpace(registryInfo.ExecutablePath) &&
                IsUserWritableLocation(registryInfo.ExecutablePath))
            {
                return $"Automatic service is stopped and its binary is in a user-writable location: {ShortenPath(registryInfo.ExecutablePath)}";
            }

            return null;
        }

        private static ServiceRegistryInfo ReadRegistryInfo(string serviceName)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                    $@"{ServicesRegistryPath}\{serviceName}");
                if (key is null)
                {
                    return ServiceRegistryInfo.Empty;
                }

                int startType = ConvertToInt32(key.GetValue("Start"), -1);
                bool delayedAutoStart = ConvertToInt32(
                    key.GetValue("DelayedAutostart"),
                    0) != 0;
                string imagePath = key.GetValue("ImagePath") as string ?? string.Empty;

                return new ServiceRegistryInfo(
                    startType,
                    delayedAutoStart,
                    ExtractExecutablePath(imagePath),
                    true);
            }
            catch
            {
                return ServiceRegistryInfo.Empty;
            }
        }

        private static string ExtractExecutablePath(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return string.Empty;
            }

            string expanded = Environment.ExpandEnvironmentVariables(imagePath.Trim());
            if (expanded.StartsWith('"'))
            {
                int closingQuote = expanded.IndexOf('"', 1);
                return closingQuote > 1
                    ? expanded[1..closingQuote]
                    : expanded.Trim('"');
            }

            int executableEnd = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return executableEnd >= 0
                ? expanded[..(executableEnd + 4)]
                : expanded;
        }

        private static bool IsTemporaryLocation(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string temp = Path.GetFullPath(Path.GetTempPath());
                return fullPath.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUserWritableLocation(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string downloads = Path.Combine(userProfile, "Downloads");

                return fullPath.StartsWith(appData, StringComparison.OrdinalIgnoreCase) ||
                       fullPath.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase) ||
                       fullPath.StartsWith(downloads, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
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

        private static string ShortenPath(string path) =>
            path.Length <= 100 ? path : "..." + path[^97..];

        private sealed record ServiceRegistryInfo(
            int StartType,
            bool DelayedAutoStart,
            string ExecutablePath,
            bool Available)
        {
            public static ServiceRegistryInfo Empty { get; } = new(-1, false, string.Empty, false);
        }

        public sealed record ServiceIntelligenceSnapshot(
            int InstalledServiceCount,
            int RunningServiceCount,
            int FlaggedServiceCount,
            string PrimaryServiceName,
            string PrimaryReason,
            bool CollectionAvailable = true)
        {
            public static ServiceIntelligenceSnapshot Unavailable { get; } =
                new(0, 0, 0, "Unavailable", "Windows service evidence could not be collected.", false);
        }

        public sealed record ServiceStatusSnapshot(
            bool Found,
            bool IsRunning,
            string Status,
            string Summary);
    }
}
