using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Sentinel.App.Services;

internal static class WindowsShellLaunchService
{
    private static readonly HashSet<string> AllowedTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "taskmgr.exe",
        "ms-settings:windowsupdate",
        "windowsdefender:",
        "windowsdefender://network",
        "services.msc",
        "ms-settings:storagesense"
    };

    internal static bool TryLaunch(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || !AllowedTargets.Contains(target))
            return false;

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
            return process is not null;
        }
        catch
        {
            return false;
        }
    }
}
