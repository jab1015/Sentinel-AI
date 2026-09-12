/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.IO;

namespace Sentinel.App.Services;

/// <summary>
/// Fail-closed policy for child processes whose side effects cannot currently be
/// bounded by BoundedProcessRunner. The runner bounds time and captured output,
/// but it cannot impose a filesystem quota on an extractor while it is running.
/// </summary>
internal static class ChildProcessSafetyPolicy
{
    internal static bool IsBlocked(ProcessStartInfoLike startInfo, out string reason)
    {
        string fileName = Path.GetFileName(startInfo.FileName ?? string.Empty);
        if (fileName.Equals("expand.exe", StringComparison.OrdinalIgnoreCase))
        {
            reason = "CAB expansion is disabled because Sentinel cannot currently bound extracted disk consumption before extraction completes.";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    // Small abstraction keeps the policy deterministic and independently testable.
    internal readonly record struct ProcessStartInfoLike(string FileName);
}
