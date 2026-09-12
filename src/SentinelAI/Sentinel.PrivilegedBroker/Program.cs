using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const int ProtocolVersion = 2;
const int MaximumRequestCharacters = 32_768;
string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SentinelAI", "Broker");
string storeRoot = Path.Combine(root, "QuarantineStore");
string recordsRoot = Path.Combine(root, "QuarantineRecords");
string transactionRoot = Path.Combine(root, "Transactions");
JsonSerializerOptions strictJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

if (!IsAdministrator()) return 20;
if (args.Length != 2 || !args[0].Equals("--pipe", StringComparison.Ordinal) ||
    !Guid.TryParseExact(args[1], "N", out _)) return 21;

string pipeName = "SentinelAI.Broker." + args[1];
BrokerResult finalResult;
try
{
    EnsureBrokerDirectories();
    QuarantineStoreEngine quarantineStore = new(
        root,
        IsProtectedWindowsPath,
        SetFileAclPrivate,
        ResetFileAclInheritance);
    quarantineStore.Recover();

    using NamedPipeServerStream pipe = new(
        pipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(30));
    if (!IsAuthorizedSentinelClient(pipe, out string callerError))
    {
        await WritePipeResultAsync(pipe, BrokerResult.Fail(string.Empty, "UnauthorizedCaller", callerError));
        return 24;
    }

    BrokerRequest? request = await ReadPipeRequestAsync(pipe);
    if (request is null || request.Version != ProtocolVersion || !Guid.TryParseExact(request.RequestId, "N", out _))
    {
        await WritePipeResultAsync(pipe, BrokerResult.Fail(request?.RequestId ?? string.Empty, "InvalidRequest", "The broker request was malformed or used an unsupported protocol version."));
        return 23;
    }

    finalResult = request.Operation switch
    {
        "quarantine-file" => MapStoreResult(request.RequestId, quarantineStore.Quarantine(request.ItemId, request.SourcePath)),
        "restore-quarantined-file" => MapStoreResult(request.RequestId, quarantineStore.Restore(request.ItemId)),
        "delete-quarantined-file" => MapStoreResult(request.RequestId, quarantineStore.Delete(request.ItemId)),
        "terminate-process" => TerminateProcess(request),
        _ => BrokerResult.Fail(request.RequestId, "UnsupportedOperation", "The requested privileged operation is not allowlisted.")
    };

    await WritePipeResultAsync(pipe, finalResult);
    return finalResult.Succeeded ? 0 : 30;
}
catch (TimeoutException) { return 25; }
catch { return 31; }

async Task<BrokerRequest?> ReadPipeRequestAsync(NamedPipeServerStream pipe)
{
    using StreamReader reader = new(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
    string? line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
    if (string.IsNullOrWhiteSpace(line) || line.Length > MaximumRequestCharacters) return null;
    try { return JsonSerializer.Deserialize<BrokerRequest>(line, strictJson); }
    catch (JsonException) { return null; }
}

async Task WritePipeResultAsync(NamedPipeServerStream pipe, BrokerResult result)
{
    using StreamWriter writer = new(pipe, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
    await writer.WriteLineAsync(JsonSerializer.Serialize(result));
}

BrokerResult MapStoreResult(string requestId, QuarantineStoreResult result) =>
    result.Succeeded
        ? BrokerResult.Ok(requestId, result.ItemId, result.Sha256, result.Message)
        : BrokerResult.Fail(requestId, result.Code, result.Message);

bool IsAuthorizedSentinelClient(NamedPipeServerStream pipe, out string error)
{
    error = string.Empty;
    try
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint pid) || pid == 0)
        {
            error = "The broker could not identify the IPC client process.";
            return false;
        }

        using Process client = Process.GetProcessById(unchecked((int)pid));
        string actual = Path.GetFullPath(client.MainModule?.FileName ?? string.Empty);
        string expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Sentinel.App.exe"));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            error = "The privileged request did not originate from the packaged Sentinel application executable.";
            return false;
        }

        return true;
    }
    catch (Exception ex)
    {
        error = $"The IPC client identity could not be verified ({ex.GetType().Name}).";
        return false;
    }
}

void EnsureBrokerDirectories()
{
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(storeRoot);
    Directory.CreateDirectory(recordsRoot);
    Directory.CreateDirectory(transactionRoot);
    SetAcl(root, usersRead: true);
    SetAcl(recordsRoot, usersRead: true);
    SetAcl(storeRoot, usersRead: false);
    SetAcl(transactionRoot, usersRead: false);
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

void ResetFileAclInheritance(string path)
{
    string icacls = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "icacls.exe");
    using Process p = new();
    p.StartInfo = new ProcessStartInfo { FileName = icacls, UseShellExecute = false, CreateNoWindow = true };
    p.StartInfo.ArgumentList.Add(path);
    p.StartInfo.ArgumentList.Add("/reset");
    if (!p.Start() || !p.WaitForExit(10_000) || p.ExitCode != 0)
        throw new InvalidOperationException("Restored file ACL inheritance could not be reset safely.");
}

bool IsProtectedWindowsPath(string path)
{
    string full = Path.GetFullPath(path);
    string windows = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string programFilesX86Raw = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    string programFilesX86 = string.IsNullOrWhiteSpace(programFilesX86Raw) ? string.Empty : Path.GetFullPath(programFilesX86Raw).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    return full.StartsWith(windows, StringComparison.OrdinalIgnoreCase) ||
           full.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) ||
           (!string.IsNullOrWhiteSpace(programFilesX86) && full.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase));
}

string ComputeSha256(string path)
{
    using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    return Convert.ToHexString(SHA256.HashData(stream));
}

bool IsAdministrator()
{
    using WindowsIdentity identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
}

[DllImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool GetNamedPipeClientProcessId(IntPtr pipeHandle, out uint clientProcessId);

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

public sealed record BrokerResult(string RequestId, bool Succeeded, string Code, string Message, string ItemId, string Sha256)
{
    public static BrokerResult Ok(string requestId, string itemId, string sha256, string message) => new(requestId, true, "Success", message, itemId, sha256);
    public static BrokerResult Fail(string requestId, string code, string message) => new(requestId, false, code, message, string.Empty, string.Empty);
}
