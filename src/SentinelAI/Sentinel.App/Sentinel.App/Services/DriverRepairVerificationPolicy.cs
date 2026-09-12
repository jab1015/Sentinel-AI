using System;

namespace Sentinel.App.Services
{
    internal static class DriverRepairVerificationPolicy
    {
        internal static bool IsExactCandidate(int deviceMatchCount, int hardwareMatchCount, int updateMatchCount) =>
            deviceMatchCount == 1 && hardwareMatchCount >= 1 && updateMatchCount == 1;

        internal static DriverInstallDecision ClassifyInstall(string overallCode, string perUpdateCode, string hresult, bool restartRequired)
        {
            if (string.Equals(overallCode, "3", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(perUpdateCode, "3", StringComparison.OrdinalIgnoreCase))
                return new(false, restartRequired, DriverInstallDisposition.Partial);

            if (!string.Equals(overallCode, "2", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(perUpdateCode, "2", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(hresult) && !string.Equals(hresult, "0", StringComparison.OrdinalIgnoreCase)))
                return new(false, restartRequired, DriverInstallDisposition.Failed);

            return restartRequired
                ? new(false, true, DriverInstallDisposition.RestartRequired)
                : new(true, false, DriverInstallDisposition.RequiresPostInstallVerification);
        }

        internal static bool IsPostInstallVerified(bool processSucceeded, bool exactDevicePresent, bool hardwareIdentityMatches,
            bool approvedUpdateStillOffered, int? deviceProblemCode) =>
            processSucceeded && exactDevicePresent && hardwareIdentityMatches && !approvedUpdateStillOffered && deviceProblemCode == 0;
    }

    internal enum DriverInstallDisposition
    {
        Failed,
        Partial,
        RestartRequired,
        RequiresPostInstallVerification
    }

    internal sealed record DriverInstallDecision(bool MayVerifyNow, bool RestartRequired, DriverInstallDisposition Disposition);
}
