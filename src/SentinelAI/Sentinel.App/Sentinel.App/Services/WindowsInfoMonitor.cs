/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    public class WindowsInfoMonitor
    {
        public string GetMachineName()
        {
            return Environment.MachineName;
        }

        public string GetUserName()
        {
            return Environment.UserName;
        }

        public string GetOsVersion()
        {
            return Environment.OSVersion.VersionString;
        }

        public bool Is64BitOperatingSystem()
        {
            return Environment.Is64BitOperatingSystem;
        }

        public int ProcessorCount()
        {
            return Environment.ProcessorCount;
        }

        public TimeSpan GetSystemUptime()
        {
            return TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
    }
}