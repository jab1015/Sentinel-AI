using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Quarantine Acceptance ===");

string root = Path.Combine(Path.GetTempPath(), "SentinelAI-QuarantineAcceptance", Guid.NewGuid().ToString("N"));
string quarantineDirectory = Path.Combine(root, "quarantine");
string sourceDirectory = Path.Combine(root, "source");
string sourcePath = Path.Combine(sourceDirectory, "sentinel-quarantine-test.txt");
string deleteSourcePath = Path.Combine(sourceDirectory, "sentinel-quarantine-delete-test.txt");

Directory.CreateDirectory(sourceDirectory);
await File.WriteAllTextAsync(sourcePath, "Sentinel AI quarantine acceptance test.");
await File.WriteAllTextAsync(deleteSourcePath, "Sentinel AI permanent deletion acceptance test.");

QuarantineService service = new(quarantineDirectory: quarantineDirectory);

try
{
    Console.WriteLine();
    Console.WriteLine("--- Scenario 1: quarantine requires approval ---");
    QuarantineService.QuarantineResult denied = await service.QuarantineAsync(
        sourcePath,
        hasVerifiedEvidence: true,
        isWindowsProtectedComponent: false,
        userApproved: false);

    bool approvalGatePass = !denied.Succeeded && denied.RequiresUserApproval && File.Exists(sourcePath);
    Console.WriteLine($"Approval gate: {(approvalGatePass ? "PASS" : "FAIL")}");

    Console.WriteLine();
    Console.WriteLine("--- Scenario 2: verified quarantine ---");
    QuarantineService.QuarantineResult quarantined = await service.QuarantineAsync(
        sourcePath,
        hasVerifiedEvidence: true,
        isWindowsProtectedComponent: false,
        userApproved: true);

    bool quarantinePass = quarantined.Succeeded && quarantined.Verified && quarantined.Record is not null && !File.Exists(sourcePath) && File.Exists(quarantined.Record.QuarantinePath);
    Console.WriteLine($"Verified quarantine: {(quarantinePass ? "PASS" : "FAIL")}");

    Console.WriteLine();
    Console.WriteLine("--- Scenario 3: restore requires approval ---");
    bool restoreApprovalPass = false;
    bool restorePass = false;

    if (quarantined.Record is not null)
    {
        QuarantineService.QuarantineResult restoreDenied = await service.RestoreAsync(quarantined.Record, userApproved: false);
        restoreApprovalPass = !restoreDenied.Succeeded && restoreDenied.RequiresUserApproval && File.Exists(quarantined.Record.QuarantinePath) && !File.Exists(sourcePath);
        Console.WriteLine($"Restore approval gate: {(restoreApprovalPass ? "PASS" : "FAIL")}");

        Console.WriteLine();
        Console.WriteLine("--- Scenario 4: verified restore / reversal ---");
        QuarantineService.QuarantineResult restored = await service.RestoreAsync(quarantined.Record, userApproved: true);
        restorePass = restored.Succeeded && restored.Verified && File.Exists(sourcePath) && !File.Exists(quarantined.Record.QuarantinePath);
        Console.WriteLine($"Verified restore: {(restorePass ? "PASS" : "FAIL")}");
    }
    else
    {
        Console.WriteLine("Restore approval gate: FAIL");
        Console.WriteLine("Verified restore: FAIL");
    }

    Console.WriteLine();
    Console.WriteLine("--- Scenario 5: permanent delete requires approval ---");
    QuarantineService.QuarantineResult deleteQuarantined = await service.QuarantineAsync(
        deleteSourcePath,
        hasVerifiedEvidence: true,
        isWindowsProtectedComponent: false,
        userApproved: true);

    bool deleteApprovalPass = false;
    bool deletePass = false;

    if (deleteQuarantined.Record is not null)
    {
        QuarantineService.QuarantineResult deleteDenied = await service.DeletePermanentlyAsync(deleteQuarantined.Record, userApproved: false);
        deleteApprovalPass = !deleteDenied.Succeeded && deleteDenied.RequiresUserApproval && File.Exists(deleteQuarantined.Record.QuarantinePath);
        Console.WriteLine($"Delete approval gate: {(deleteApprovalPass ? "PASS" : "FAIL")}");

        Console.WriteLine();
        Console.WriteLine("--- Scenario 6: verified permanent deletion ---");
        QuarantineService.QuarantineResult deleted = await service.DeletePermanentlyAsync(deleteQuarantined.Record, userApproved: true);
        deletePass = deleted.Succeeded && deleted.Verified && !File.Exists(deleteQuarantined.Record.QuarantinePath) && !File.Exists(deleteSourcePath);
        Console.WriteLine($"Verified permanent deletion: {(deletePass ? "PASS" : "FAIL")}");
    }
    else
    {
        Console.WriteLine("Delete approval gate: FAIL");
        Console.WriteLine("Verified permanent deletion: FAIL");
    }

    bool pass = approvalGatePass && quarantinePass && restoreApprovalPass && restorePass && deleteApprovalPass && deletePass;

    Console.WriteLine();
    Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
    Environment.ExitCode = pass ? 0 : 1;
}
finally
{
    try
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
    catch
    {
        // Acceptance cleanup must not change the functional test result.
    }
}
