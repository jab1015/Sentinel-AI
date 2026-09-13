using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

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

        if (!TryResolveTrustedTarget(target, out string resolvedTarget))
            return false;

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = resolvedTarget,
                UseShellExecute = true
            });
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveTrustedTarget(string target, out string resolvedTarget)
    {
        resolvedTarget = string.Empty;
        if (target.Equals("taskmgr.exe", StringComparison.OrdinalIgnoreCase))
            return TryResolveSystemFile("Taskmgr.exe", out resolvedTarget);
        if (target.Equals("services.msc", StringComparison.OrdinalIgnoreCase))
            return TryResolveSystemFile("services.msc", out resolvedTarget);

        if (target.Equals("ms-settings:windowsupdate", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("windowsdefender:", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("windowsdefender://network", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("ms-settings:storagesense", StringComparison.OrdinalIgnoreCase))
        {
            resolvedTarget = target;
            return true;
        }

        return false;
    }

    private static bool TryResolveSystemFile(string fileName, out string resolvedPath)
    {
        resolvedPath = Path.Combine(Environment.SystemDirectory, fileName);
        return Path.IsPathFullyQualified(resolvedPath) && File.Exists(resolvedPath);
    }
}
