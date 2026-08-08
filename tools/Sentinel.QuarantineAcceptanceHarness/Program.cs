using Sentinel.App.Services;

Console.WriteLine("=== Sentinel AI Quarantine Acceptance ===");

string root = Path.Combine(Path.GetTempPath(), "SentinelAI-QuarantineAcceptance", Guid.NewGuid().ToString("N"));
string quarantineDirectory = Path.Combine(root, "quarantine");
string sourceDirectory = Path.Combine(root, "source");
string catalogPath = Path.Combine(root, "catalog.json");
string sourcePath = Path.Combine(sourceDirectory, "sentinel-quarantine-test.txt");
string deleteSourcePath = Path.Combine(sourceDirectory, "sentinel-quarantine-delete-test.txt");
string tamperSourcePath = Path.Combine(sourceDirectory, "sentinel-quarantine-tamper-test.txt");

Directory.CreateDirectory(sourceDirectory);
await File.WriteAllTextAsync(sourcePath, "Sentinel AI quarantine acceptance test.");
await File.WriteAllTextAsync(deleteSourcePath, "Sentinel AI permanent deletion acceptance test.");
await File.WriteAllTextAsync(tamperSourcePath, "Sentinel AI quarantine tamper acceptance test.");

QuarantineService service = new(quarantineDirectory: quarantineDirectory);
QuarantineCatalogService catalog = new(catalogPath);

try
{
    Console.WriteLine();
    Console.WriteLine("--- Scenario 1: quarantine requires approval ---");
    QuarantineService.QuarantineResult denied = await service.QuarantineAsync(
        sourcePath,
        hasVerifiedEvidence: true,
        isWindowsProtectedComponent: false,
        userApproved: false);

    bool approvalGatePass = !denied.Succeeded && !denied.Attempted && denied.RequiresUserApproval && File.Exists(sourcePath);
    Console.WriteLine($"Approval gate: {(approvalGatePass ? "PASS" : "FAIL")}");

    Console.WriteLine();
    Console.WriteLine("--- Scenario 2: verified quarantine and catalog registration ---");
    QuarantineService.QuarantineResult quarantined = await service.QuarantineAsync(
        sourcePath,
        hasVerifiedEvidence: true,
        isWindowsProtectedComponent: false,
        userApproved: true);

    bool quarantinePass = quarantined.Attempted && quarantined.Succeeded && quarantined.Verified && quarantined.Record is not null && !File.Exists(sourcePath) && File.Exists(quarantined.Record.QuarantinePath);
    Console.WriteLine($"Verified quarantine: {(quarantinePass ? "PASS" : "FAIL")}");

    bool catalogAddPass = false;
    bool catalogReconcilePass = false;

    if (quarantined.Record is not null)
    {
        await catalog.AddAsync(quarantined.Record);
        IReadOnlyList<QuarantineCatalogService.QuarantineCatalogEntry> entries = await catalog.GetEntriesAsync();
        catalogAddPass = entries.Count == 1 && entries[0].IsPresent && entries[0].Sha256 == quarantined.Record.Sha256;
        Console.WriteLine($"Catalog registration: {(catalogAddPass ? "PASS" : "FAIL")}");

        IReadOnlyList<QuarantineCatalogService.QuarantineCatalogEntry> reconciled = await catalog.ReconcileAsync();
        catalogReconcilePass = reconciled.Count == 1 && reconciled[0].IsPresent;
        Console.WriteLine($"Catalog reconcile: {(catalogReconcilePass ? "PASS" : "FAIL")}");
    }
    else
    {
        Console.WriteLine("Catalog registration: FAIL");
        Console.WriteLine("Catalog reconcile: FAIL");
    }

    Console.WriteLine();
    Console.WriteLine("--- Scenario 3: restore requires approval ---");
    bool restoreApprovalPass = false;
    bool restorePass = false;
    bool restoreCatalogPass = false;

    if (quarantined.Record is not null)
    {
        QuarantineService.QuarantineResult restoreDenied = await service.RestoreAsync(quarantined.Record, userApproved: false);
        restoreApprovalPass = !restoreDenied.Succeeded && !restoreDenied.Attempted && restoreDenied.RequiresUserApproval && File.Exists(quarantined.Record.QuarantinePath) && !File.Exists(sourcePath);
        Console.WriteLine($"Restore approval gate: {(restoreApprovalPass ? "PASS" : "FAIL")}");

        Console.WriteLine();
        Console.WriteLine("--- Scenario 4: verified restore / reversal ---");
        QuarantineService.QuarantineResult restored = await service.RestoreAsync(quarantined.Record, userApproved: true);
        restorePass = restored.Attempted && restored.Succeeded && restored.Verified && File.Exists(sourcePath) && !File.Exists(quarantined.Record.QuarantinePath);
        Console.WriteLine($"Verified restore: {(restorePass ? "PASS" : "FAIL")}");

        await catalog.RemoveAsync(quarantined.Record.QuarantinePath);
        restoreCatalogPass = (await catalog.GetEntriesAsync()).Count == 0;
        Console.WriteLine($"Catalog removal after restore: {(restoreCatalogPass ? "PASS" : "FAIL")}");
    }
    else
    {
        Console.WriteLine("Restore approval gate: FAIL");
        Console.WriteLine("Verified restore: FAIL");
        Console.WriteLine("Catalog removal after restore: FAIL");
    }

    Console.WriteLine();
    Console.WriteLine("--- Scenario 5: permanent delete requires approval ---");
    QuarantineService.QuarantineResult deleteQuarantined = await service.QuarantineAsync(
        deleteSourcePath,
        hasVerifiedEvidence: true,
        isWindowsProtectedComponent: false,
        userApproved: true);

    bool deleteCatalogAddPass = false;
    bool deleteApprovalPass = false;
    bool deletePass = false;
    bool deleteCatalogPass = false;

    if (deleteQuarantined.Record is not null)
    {
        await catalog.AddAsync(deleteQuarantined.Record);
        deleteCatalogAddPass = (await catalog.GetEntriesAsync()).Count == 1;
        Console.WriteLine($"Delete test catalog registration: {(deleteCatalogAddPass ? "PASS" : "FAIL")}");

        QuarantineService.QuarantineResult deleteDenied = await service.DeletePermanentlyAsync(deleteQuarantined.Record, userApproved: false);
        deleteApprovalPass = !deleteDenied.Succeeded && !deleteDenied.Attempted && deleteDenied.RequiresUserApproval && File.Exists(deleteQuarantined.Record.QuarantinePath);
        Console.WriteLine($"Delete approval gate: {(deleteApprovalPass ? "PASS" : "FAIL")}");

        Console.WriteLine();
        Console.WriteLine("--- Scenario 6: verified permanent deletion ---");
        QuarantineService.QuarantineResult deleted = await service.DeletePermanentlyAsync(deleteQuarantined.Record, userApproved: true);
        deletePass = deleted.Attempted && deleted.Succeeded && deleted.Verified && !File.Exists(deleteQuarantined.Record.QuarantinePath) && !File.Exists(deleteSourcePath);
        Console.WriteLine($"Verified permanent deletion: {(deletePass ? "PASS" : "FAIL")}");

        await catalog.RemoveAsync(deleteQuarantined.Record.QuarantinePath);
        deleteCatalogPass = (await catalog.GetEntriesAsync()).Count == 0;
        Console.WriteLine($"Catalog removal after delete: {(deleteCatalogPass ? "PASS" : "FAIL")}");
    }
    else
    {
        Console.WriteLine("Delete test catalog registration: FAIL");
        Console.WriteLine("Delete approval gate: FAIL");
        Console.WriteLine("Verified permanent deletion: FAIL");
        Console.WriteLine("Catalog removal after delete: FAIL");
    }

    Console.WriteLine();
    Console.WriteLine("--- Scenario 7: tampered quarantine identity is refused ---");
    QuarantineService.QuarantineResult tamperQuarantined = await service.QuarantineAsync(
        tamperSourcePath,
        hasVerifiedEvidence: true,
        isWindowsProtectedComponent: false,
        userApproved: true);

    bool tamperedRestoreRefused = false;
    bool tamperedDeleteRefused = false;
    if (tamperQuarantined.Record is not null)
    {
        await File.AppendAllTextAsync(
            tamperQuarantined.Record.QuarantinePath,
            " changed after quarantine");

        QuarantineService.QuarantineResult tamperedRestore =
            await service.RestoreAsync(tamperQuarantined.Record, userApproved: true);
        tamperedRestoreRefused =
            !tamperedRestore.Succeeded &&
            !tamperedRestore.Attempted &&
            File.Exists(tamperQuarantined.Record.QuarantinePath) &&
            !File.Exists(tamperSourcePath);
        Console.WriteLine($"Tampered restore refused: {(tamperedRestoreRefused ? "PASS" : "FAIL")}");

        QuarantineService.QuarantineResult tamperedDelete =
            await service.DeletePermanentlyAsync(tamperQuarantined.Record, userApproved: true);
        tamperedDeleteRefused =
            !tamperedDelete.Succeeded &&
            tamperedDelete.Attempted &&
            File.Exists(tamperQuarantined.Record.QuarantinePath);
        Console.WriteLine($"Tampered deletion refused and rolled back: {(tamperedDeleteRefused ? "PASS" : "FAIL")}");
    }
    else
    {
        Console.WriteLine("Tampered restore refused: FAIL");
        Console.WriteLine("Tampered deletion refused and rolled back: FAIL");
    }

    bool pass = approvalGatePass && quarantinePass && catalogAddPass && catalogReconcilePass &&
        restoreApprovalPass && restorePass && restoreCatalogPass && deleteCatalogAddPass &&
        deleteApprovalPass && deletePass && deleteCatalogPass &&
        tamperedRestoreRefused && tamperedDeleteRefused;

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
