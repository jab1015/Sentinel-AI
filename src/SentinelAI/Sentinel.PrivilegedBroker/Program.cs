using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

const int ProtocolVersion = 1;
string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SentinelAI", "Broker");
string storeRoot = Path.Combine(root, "QuarantineStore");
string recordsRoot = Path.Combine(root, "QuarantineRecords");
string transactionRoot = Path.Combine(root, "Transactions");
string resultsRoot = Path.Combine(root, "Results");

if (!IsAdministrator()) return 20;
if (args.Length != 2 || !args[0].Equals("--request", StringComparison.Ordinal)) return 21;

BrokerRequest? request;
try
{
    byte[] requestBytes = Convert.FromBase64String(args[1]);
    if (requestBytes.Length > 32_768) return 22;
    request = JsonSerializer.Deserialize<BrokerRequest>(requestBytes);
}
catch { return 22; }

if (request is null || request.Version != ProtocolVersion || !Guid.TryParseExact(request.RequestId, "N", out _)) return 23;

try
{
    EnsureBrokerDirectories();
    RecoverQuarantineTransactions();

    BrokerResult result = request.Operation switch
    {
        "quarantine-file" => Quarantine(request),
        "restore-quarantined-file" => Restore(request),
        "delete-quarantined-file" => DeleteQuarantined(request),
        "terminate-process" => TerminateProcess(request),
        _ => BrokerResult.Fail(request.RequestId, "UnsupportedOperation", "The requested privileged operation is not allowlisted.")
    };

    WriteResult(result);
    return result.Succeeded ? 0 : 30;
}
catch (Exception ex)
{
    try { WriteResult(BrokerResult.Fail(request.RequestId, "BrokerFailure", ex.GetType().Name)); } catch { }
    return 31;
}

void EnsureBrokerDirectories()
{
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(storeRoot);
    Directory.CreateDirectory(recordsRoot);
    Directory.CreateDirectory(transactionRoot);
    Directory.CreateDirectory(resultsRoot);

    // Root/records/results are readable but not writable by ordinary users. Payloads
    // and transaction state are accessible only to SYSTEM and Administrators.
    SetAcl(root, usersRead: true);
    SetAcl(recordsRoot, usersRead: true);
    SetAcl(resultsRoot, usersRead: true);
    SetAcl(storeRoot, usersRead: false);
    SetAcl(transactionRoot, usersRead: false);
}

BrokerResult Quarantine(BrokerRequest req)
{
    if (!TryValidateItemId(req.ItemId, out string itemId))
        return BrokerResult.Fail(req.RequestId, "InvalidItemId", "The quarantine item ID is invalid.");
    if (!TryCanonicalizeSource(req.SourcePath, out string sourcePath, out string sourceError))
        return BrokerResult.Fail(req.RequestId, "InvalidSource", sourceError);

    string payloadPath = PayloadPath(itemId);
    string recordPath = RecordPath(itemId);
    string transactionPath = TransactionPath(itemId);
    if (File.Exists(payloadPath) || File.Exists(recordPath) || File.Exists(transactionPath))
        return BrokerResult.Fail(req.RequestId, "ItemAlreadyExists", "The quarantine item ID is already in use.");

    string hash = ComputeSha256(sourcePath);
    QuarantineTransaction txn = new(itemId, "Quarantine", "Prepared", sourcePath, hash, DateTimeOffset.UtcNow);
    WriteAtomicJson(transactionPath, txn);

    string tempPayload = Path.Combine(storeRoot, itemId + ".tmp");
    File.Copy(sourcePath, tempPayload, overwrite: false);
    SetFileAclPrivate(tempPayload);
    string copiedHash = ComputeSha256(tempPayload);
    if (!hash.Equals(copiedHash, StringComparison.OrdinalIgnoreCase))
    {
        SafeDelete(tempPayload);
        SafeDelete(transactionPath);
        return BrokerResult.Fail(req.RequestId, "CopyVerificationFailed", "The protected quarantine copy did not match the source file.");
    }

    File.Move(tempPayload, payloadPath);
    txn = txn with { Stage = "PayloadReady" };
    WriteAtomicJson(transactionPath, txn);

    File.Delete(sourcePath);
    if (File.Exists(sourcePath) || !File.Exists(payloadPath) || !ComputeSha256(payloadPath).Equals(hash, StringComparison.OrdinalIgnoreCase))
        return BrokerResult.Fail(req.RequestId, "ContainmentVerificationFailed", "The file move could not be verified. Recovery state was preserved.");

    QuarantineRecord record = new(itemId, sourcePath, hash, DateTimeOffset.UtcNow);
    WriteAtomicJson(recordPath, record);
    SafeDelete(transactionPath);
    return BrokerResult.Ok(req.RequestId, itemId, hash, "The file was moved into the protected quarantine store and verified.");
}

BrokerResult Restore(BrokerRequest req)
{
    if (!TryValidateItemId(req.ItemId, out string itemId))
        return BrokerResult.Fail(req.RequestId, "InvalidItemId", "The quarantine item ID is invalid.");

    QuarantineRecord? record = ReadRecord(itemId);
    if (record is null) return BrokerResult.Fail(req.RequestId, "RecordMissing", "The protected quarantine record was not found.");

    string payloadPath = PayloadPath(itemId);
    if (!File.Exists(payloadPath)) return BrokerResult.Fail(req.RequestId, "PayloadMissing", "The quarantined payload was not found.");
    if (!ComputeSha256(payloadPath).Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
        return BrokerResult.Fail(req.RequestId, "PayloadTampered", "The quarantined payload no longer matches its protected record.");

    string destination = Path.GetFullPath(record.OriginalPath);
    if (IsProtectedWindowsPath(destination))
        return BrokerResult.Fail(req.RequestId, "ProtectedDestination", "Sentinel will not restore a quarantine item into a protected Windows or Program Files location through this broker version.");
    if (File.Exists(destination))
        return BrokerResult.Fail(req.RequestId, "RestoreCollision", "A file already exists at the original location. Sentinel will not overwrite it.");

    string? parent = Path.GetDirectoryName(destination);
    if (string.IsNullOrWhiteSpace(parent)) return BrokerResult.Fail(req.RequestId, "InvalidDestination", "The restore destination is invalid.");
    Directory.CreateDirectory(parent);
    if (HasReparsePointInExistingPath(parent))
        return BrokerResult.Fail(req.RequestId, "ReparsePointRejected", "The restore path contains a junction or symbolic link and was rejected.");

    QuarantineTransaction txn = new(itemId, "Restore", "Prepared", destination, record.Sha256, DateTimeOffset.UtcNow);
    WriteAtomicJson(TransactionPath(itemId), txn);

    string tempDestination = destination + ".sentinel-restore-" + Guid.NewGuid().ToString("N") + ".tmp";
    File.Copy(payloadPath, tempDestination, overwrite: false);
    if (!ComputeSha256(tempDestination).Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
    {
        SafeDelete(tempDestination);
        SafeDelete(TransactionPath(itemId));
        return BrokerResult.Fail(req.RequestId, "RestoreCopyVerificationFailed", "The restored copy did not match the quarantine record.");
    }

    File.Move(tempDestination, destination);
    txn = txn with { Stage = "DestinationReady" };
    WriteAtomicJson(TransactionPath(itemId), txn);

    if (!File.Exists(destination) || !ComputeSha256(destination).Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
        return BrokerResult.Fail(req.RequestId, "RestoreVerificationFailed", "The restored file could not be verified. Recovery state was preserved.");

    File.Delete(payloadPath);
    SafeDelete(RecordPath(itemId));
    SafeDelete(TransactionPath(itemId));
    return BrokerResult.Ok(req.RequestId, itemId, record.Sha256, "The file was restored and verified without overwriting an existing file.");
}

BrokerResult DeleteQuarantined(BrokerRequest req)
{
    if (!TryValidateItemId(req.ItemId, out string itemId))
        return BrokerResult.Fail(req.RequestId, "InvalidItemId", "The quarantine item ID is invalid.");
    QuarantineRecord? record = ReadRecord(itemId);
    if (record is null) return BrokerResult.Fail(req.RequestId, "RecordMissing", "The protected quarantine record was not found.");
    string payloadPath = PayloadPath(itemId);
    if (!File.Exists(payloadPath)) return BrokerResult.Fail(req.RequestId, "PayloadMissing", "The quarantined payload was not found.");
    if (!ComputeSha256(payloadPath).Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
        return BrokerResult.Fail(req.RequestId, "PayloadTampered", "The quarantined payload no longer matches its protected record.");

    QuarantineTransaction txn = new(itemId, "Delete", "Prepared", record.OriginalPath, record.Sha256, DateTimeOffset.UtcNow);
    WriteAtomicJson(TransactionPath(itemId), txn);
    File.Delete(payloadPath);
    if (File.Exists(payloadPath)) return BrokerResult.Fail(req.RequestId, "DeleteVerificationFailed", "Permanent deletion could not be verified.");
    SafeDelete(RecordPath(itemId));
    SafeDelete(TransactionPath(itemId));
    return BrokerResult.Ok(req.RequestId, itemId, record.Sha256, "The quarantined payload was permanently deleted and verified absent.");
}

BrokerResult TerminateProcess(BrokerRequest req)
{
    if (req.ProcessId <= 0 || req.ExpectedProcessStartUtcTicks <= 0 || string.IsNullOrWhiteSpace(req.ExpectedImagePath) || string.IsNullOrWhiteSpace(req.ExpectedImageSha256))
        return BrokerResult.Fail(req.RequestId, "IncompleteProcessIdentity", "Exact process identity evidence is required.");

    using Process process = Process.GetProcessById(req.ProcessId);
    long actualStartTicks = process.StartTime.ToUniversalTime().Ticks;
    if (actualStartTicks != req.ExpectedProcessStartUtcTicks)
        return BrokerResult.Fail(req.RequestId, "ProcessIdentityChanged", "The PID now belongs to a different process instance.");

    string actualPath = Path.GetFullPath(process.MainModule?.FileName ?? string.Empty);
    string expectedPath = Path.GetFullPath(req.ExpectedImagePath);
    if (!actualPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
        return BrokerResult.Fail(req.RequestId, "ProcessImageChanged", "The process image path no longer matches the approved target.");

    string actualHash = ComputeSha256(actualPath);
    if (!actualHash.Equals(req.ExpectedImageSha256, StringComparison.OrdinalIgnoreCase))
        return BrokerResult.Fail(req.RequestId, "ProcessImageChanged", "The process image content no longer matches the approved target.");

    process.Kill(entireProcessTree: req.TerminateDescendants);
    if (!process.WaitForExit(10_000))
        return BrokerResult.Fail(req.RequestId, "TerminationUnverified", "The approved process did not exit within the verification window.");

    return BrokerResult.Ok(req.RequestId, string.Empty, actualHash,
        req.TerminateDescendants ? "The exact approved process instance and its descendants were terminated." : "The exact approved process instance was terminated.");
}

void RecoverQuarantineTransactions()
{
    foreach (string transactionPath in Directory.EnumerateFiles(transactionRoot, "*.json", SearchOption.TopDirectoryOnly))
    {
        QuarantineTransaction? txn;
        try { txn = JsonSerializer.Deserialize<QuarantineTransaction>(File.ReadAllText(transactionPath)); }
        catch { continue; }
        if (txn is null || !TryValidateItemId(txn.ItemId, out string itemId)) continue;

        string payloadPath = PayloadPath(itemId);
        string recordPath = RecordPath(itemId);
        if (txn.Operation == "Quarantine")
        {
            bool sourceExists = File.Exists(txn.Path);
            bool payloadExists = File.Exists(payloadPath);
            if (payloadExists && !sourceExists && ComputeSha256(payloadPath).Equals(txn.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(recordPath)) WriteAtomicJson(recordPath, new QuarantineRecord(itemId, txn.Path, txn.Sha256, txn.CreatedAtUtc));
                SafeDelete(transactionPath);
            }
            else if (payloadExists && sourceExists)
            {
                SafeDelete(payloadPath);
                SafeDelete(transactionPath);
            }
            else if (!payloadExists && sourceExists)
            {
                SafeDelete(transactionPath);
            }
        }
        else if (txn.Operation == "Restore")
        {
            bool destinationExists = File.Exists(txn.Path);
            bool payloadExists = File.Exists(payloadPath);
            if (destinationExists && payloadExists && ComputeSha256(txn.Path).Equals(txn.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                SafeDelete(payloadPath);
                SafeDelete(recordPath);
                SafeDelete(transactionPath);
            }
            else if (!destinationExists && payloadExists)
            {
                SafeDelete(transactionPath);
            }
        }
        else if (txn.Operation == "Delete" && !File.Exists(payloadPath))
        {
            SafeDelete(recordPath);
            SafeDelete(transactionPath);
        }
    }
}

QuarantineRecord? ReadRecord(string itemId)
{
    string path = RecordPath(itemId);
    if (!File.Exists(path)) return null;
    try
    {
        QuarantineRecord? record = JsonSerializer.Deserialize<QuarantineRecord>(File.ReadAllText(path));
        return record is not null && record.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase) ? record : null;
    }
    catch { return null; }
}

void WriteResult(BrokerResult result)
{
    string path = Path.Combine(resultsRoot, result.RequestId + ".json");
    WriteAtomicJson(path, result);
}

void WriteAtomicJson<T>(string path, T value)
{
    string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
    File.WriteAllText(temp, JsonSerializer.Serialize(value), new UTF8Encoding(false));
    using (FileStream stream = new(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) stream.Flush(true);
    File.Move(temp, path, overwrite: true);
}

void SetAcl(string path, bool usersRead)
{
    string icacls = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "icacls.exe");
    using Process p = new();
    p.StartInfo = new ProcessStartInfo { FileName = icacls, UseShellExecute = false, CreateNoWindow = true };
    p.StartInfo.ArgumentList.Add(path);
    p.StartInfo.ArgumentList.Add("/inheritance:r");
    p.StartInfo.ArgumentList.Add("/grant:r");
    p.StartInfo.ArgumentList.Add("*S-1-5-18:(OI)(CI)(F)");
    p.StartInfo.ArgumentList.Add("*S-1-5-32-544:(OI)(CI)(F)");
    if (usersRead) p.StartInfo.ArgumentList.Add("*S-1-5-32-545:(OI)(CI)(RX)");
    if (!p.Start() || !p.WaitForExit(10_000) || p.ExitCode != 0) throw new InvalidOperationException("ACL configuration failed.");
}

void SetFileAclPrivate(string path)
{
    string icacls = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "icacls.exe");
    using Process p = new();
    p.StartInfo = new ProcessStartInfo { FileName = icacls, UseShellExecute = false, CreateNoWindow = true };
    p.StartInfo.ArgumentList.Add(path);
    p.StartInfo.ArgumentList.Add("/inheritance:r");
    p.StartInfo.ArgumentList.Add("/grant:r");
    p.StartInfo.ArgumentList.Add("*S-1-5-18:(F)");
    p.StartInfo.ArgumentList.Add("*S-1-5-32-544:(F)");
    if (!p.Start() || !p.WaitForExit(10_000) || p.ExitCode != 0) throw new InvalidOperationException("Payload ACL configuration failed.");
}

bool TryCanonicalizeSource(string? raw, out string fullPath, out string error)
{
    fullPath = string.Empty;
    error = string.Empty;
    if (string.IsNullOrWhiteSpace(raw)) { error = "The source path is empty."; return false; }
    try { fullPath = Path.GetFullPath(raw); }
    catch { error = "The source path is invalid."; return false; }
    if (!File.Exists(fullPath)) { error = "The source file does not exist."; return false; }
    if (IsProtectedWindowsPath(fullPath)) { error = "Protected Windows and Program Files locations are not eligible for direct Sentinel quarantine."; return false; }
    string? parent = Path.GetDirectoryName(fullPath);
    if (string.IsNullOrWhiteSpace(parent) || HasReparsePointInExistingPath(parent)) { error = "The source path contains a junction or symbolic link and was rejected."; return false; }
    return true;
}

bool IsProtectedWindowsPath(string path)
{
    string full = Path.GetFullPath(path);
    string windows = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string programFilesX86 = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    return full.StartsWith(windows, StringComparison.OrdinalIgnoreCase) || full.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(programFilesX86) && full.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase));
}

bool HasReparsePointInExistingPath(string path)
{
    DirectoryInfo? current = new(Path.GetFullPath(path));
    while (current is not null && current.Exists)
    {
        if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        current = current.Parent;
    }
    return false;
}

bool TryValidateItemId(string? value, out string itemId)
{
    itemId = string.Empty;
    if (!Guid.TryParseExact(value, "N", out Guid id)) return false;
    itemId = id.ToString("N");
    return true;
}

string PayloadPath(string itemId) => Path.Combine(storeRoot, itemId + ".sentinelq");
string RecordPath(string itemId) => Path.Combine(recordsRoot, itemId + ".json");
string TransactionPath(string itemId) => Path.Combine(transactionRoot, itemId + ".json");

string ComputeSha256(string path)
{
    using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    return Convert.ToHexString(SHA256.HashData(stream));
}

void SafeDelete(string path)
{
    try { if (File.Exists(path)) File.Delete(path); } catch { }
}

bool IsAdministrator()
{
    using WindowsIdentity identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
}

public sealed record BrokerRequest(
    int Version,
    string RequestId,
    string Operation,
    string? ItemId = null,
    string? SourcePath = null,
    int ProcessId = 0,
    long ExpectedProcessStartUtcTicks = 0,
    string? ExpectedImagePath = null,
    string? ExpectedImageSha256 = null,
    bool TerminateDescendants = false);

public sealed record BrokerResult(
    string RequestId,
    bool Succeeded,
    string Code,
    string Message,
    string ItemId,
    string Sha256)
{
    public static BrokerResult Ok(string requestId, string itemId, string sha256, string message) => new(requestId, true, "Success", message, itemId, sha256);
    public static BrokerResult Fail(string requestId, string code, string message) => new(requestId, false, code, message, string.Empty, string.Empty);
}

public sealed record QuarantineRecord(string ItemId, string OriginalPath, string Sha256, DateTimeOffset QuarantinedAtUtc);
public sealed record QuarantineTransaction(string ItemId, string Operation, string Stage, string Path, string Sha256, DateTimeOffset CreatedAtUtc);
