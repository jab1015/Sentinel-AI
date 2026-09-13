using System;
using System.IO;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Fail-closed canonicalization boundary for paths read from quarantine metadata.
    /// Protected metadata is security evidence, not an exception-safe assumption: corrupt
    /// or malformed paths must invalidate the record rather than escape verification.
    /// </summary>
    internal static class QuarantineRecordPathPolicy
    {
        internal static bool TryCanonicalize(string? value, out string canonicalPath)
        {
            canonicalPath = string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return false;

            try
            {
                canonicalPath = Path.GetFullPath(value);
                return !string.IsNullOrWhiteSpace(canonicalPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                canonicalPath = string.Empty;
                return false;
            }
        }
    }
}
