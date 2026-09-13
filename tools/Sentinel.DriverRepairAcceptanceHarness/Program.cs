using Sentinel.App.Services;

static void Require(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

Require(DriverRepairVerificationPolicy.IsExactCandidate(1, 1, 1), "exact device/hardware/update candidate accepted");
Require(!DriverRepairVerificationPolicy.IsExactCandidate(0, 1, 1), "missing device rejected");
Require(!DriverRepairVerificationPolicy.IsExactCandidate(2, 1, 1), "ambiguous device rejected");
Require(!DriverRepairVerificationPolicy.IsExactCandidate(1, 0, 1), "wrong hardware identity rejected");
Require(!DriverRepairVerificationPolicy.IsExactCandidate(1, 1, 0), "missing approved update rejected");
Require(!DriverRepairVerificationPolicy.IsExactCandidate(1, 1, 2), "ambiguous approved update rejected");

DriverInstallDecision clean = DriverRepairVerificationPolicy.ClassifyInstall("2", "2", "0", false);
Require(clean.MayVerifyNow && clean.Disposition == DriverInstallDisposition.RequiresPostInstallVerification, "clean installer result requires post-install verification");
Require(DriverRepairVerificationPolicy.ClassifyInstall("3", "2", "0", false).Disposition == DriverInstallDisposition.Partial, "overall success-with-errors rejected as verified");
Require(DriverRepairVerificationPolicy.ClassifyInstall("2", "3", "0", false).Disposition == DriverInstallDisposition.Partial, "per-update success-with-errors rejected as verified");
Require(DriverRepairVerificationPolicy.ClassifyInstall("4", "2", "0", false).Disposition == DriverInstallDisposition.Failed, "overall failure rejected");
Require(DriverRepairVerificationPolicy.ClassifyInstall("2", "4", "0", false).Disposition == DriverInstallDisposition.Failed, "per-update failure rejected");
Require(DriverRepairVerificationPolicy.ClassifyInstall("2", "2", unchecked((int)0x80004005).ToString(), false).Disposition == DriverInstallDisposition.Failed, "nonzero HRESULT rejected");
DriverInstallDecision reboot = DriverRepairVerificationPolicy.ClassifyInstall("2", "2", "0", true);
Require(!reboot.MayVerifyNow && reboot.RestartRequired && reboot.Disposition == DriverInstallDisposition.RestartRequired, "restart-required result not marked verified");

Require(DriverRepairVerificationPolicy.HasDriverVersionChanged("1.0.0.0", "1.0.1.0"), "changed driver version accepted as evidence");
Require(!DriverRepairVerificationPolicy.HasDriverVersionChanged("1.0.0.0", "1.0.0.0"), "unchanged driver version rejected");
Require(!DriverRepairVerificationPolicy.HasDriverVersionChanged("", "1.0.1.0"), "missing pre-install driver version rejected");
Require(!DriverRepairVerificationPolicy.HasDriverVersionChanged("1.0.0.0", ""), "missing post-install driver version rejected");

Require(DriverRepairVerificationPolicy.IsPostInstallVerified(true, true, true, false, 0, "1.0.0.0", "1.0.1.0"), "healthy exact post-install state with changed version accepted");
Require(!DriverRepairVerificationPolicy.IsPostInstallVerified(false, true, true, false, 0, "1.0.0.0", "1.0.1.0"), "verification process failure rejected");
Require(!DriverRepairVerificationPolicy.IsPostInstallVerified(true, false, true, false, 0, "1.0.0.0", "1.0.1.0"), "changed device rejected post-install");
Require(!DriverRepairVerificationPolicy.IsPostInstallVerified(true, true, false, false, 0, "1.0.0.0", "1.0.1.0"), "changed hardware identity rejected post-install");
Require(!DriverRepairVerificationPolicy.IsPostInstallVerified(true, true, true, true, 0, "1.0.0.0", "1.0.1.0"), "approved update still offered rejected post-install");
Require(!DriverRepairVerificationPolicy.IsPostInstallVerified(true, true, true, false, 28, "1.0.0.0", "1.0.1.0"), "device problem code rejected post-install");
Require(!DriverRepairVerificationPolicy.IsPostInstallVerified(true, true, true, false, null, "1.0.0.0", "1.0.1.0"), "missing device health evidence rejected post-install");
Require(!DriverRepairVerificationPolicy.IsPostInstallVerified(true, true, true, false, 0, "1.0.0.0", "1.0.0.0"), "unchanged driver version rejected post-install");

Console.WriteLine("Sentinel driver repair A10/A11 acceptance harness passed.");
