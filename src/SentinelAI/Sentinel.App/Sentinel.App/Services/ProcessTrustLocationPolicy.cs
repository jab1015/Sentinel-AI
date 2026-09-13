using System;
using System.IO;

namespace Sentinel.App.Services;

internal static class ProcessTrustLocationPolicy
{
    internal static bool IsTypicallyUserWritableLocation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;

        try
        {
            string fullPath = Path.GetFullPath(path);
            if (IsWithinConfiguredDirectory(fullPath, Path.GetTempPath())) return true;

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (IsWithinConfiguredDirectory(fullPath, userProfile)) return true;

            // Keep these explicit roots because profile redirection can place them outside the
            // nominal user profile root on managed or redirected Windows installations.
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (IsWithinConfiguredDirectory(fullPath, appData)) return true;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (IsWithinConfiguredDirectory(fullPath, localAppData)) return true;

            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsWithinConfiguredDirectory(string fullPath, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        string normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
