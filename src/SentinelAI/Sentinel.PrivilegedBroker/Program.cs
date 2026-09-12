using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const int ProtocolVersion = 2;
const int ErrorInsufficientBuffer = 122;
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
    ServiceRestartEngine serviceRestart = new(transactionRoot);
    IReadOnlyList<QuarantineStoreIssue> recoveryGuardIssues = QuarantineRecoveryGuard.Validate(root);
    if (recoveryGuardIssues.Count == 0)
    {
        quarantineStore.Recover();
    }
    ServiceRestartResult serviceRecovery = serviceRestart.RecoverPending();

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
        "firewall-block-endpoint" => ApplyFirewallMutation(request, add: true),
        "firewall-remove-endpoint" => ApplyFirewallMutation(request, add: false),
        "restart-service" => serviceRecovery.Succeeded
            ? MapServiceRestartResult(request.RequestId, serviceRestart.Restart(request.ServiceName))
            : BrokerResult.Fail(request.RequestId, serviceRecovery.Code, serviceRecovery.Message),
        _ => BrokerResult.Fail(request.RequestId, "UnsupportedOperation", "The requested privileged operation is not allowlisted.")
    };

    await WritePipeResultAsync(pipe, finalResult);
    return finalResult.Succeeded ? 0 : 30;
}
catch (TimeoutException) { return 25; }
catch (OperationCanceledException) { return 25; }
catch { return 31; }

async Task<BrokerRequest?> ReadPipeRequestAsync(NamedPipeServerStream pipe)
{
    string? line = await BrokerPipeMessageIO.ReadBoundedLineAsync(pipe);
    if (string.IsNullOrWhiteSpace(line)) return null;
    try { return JsonSerializer.Deserialize<BrokerRequest>(line, strictJson); }
    catch (JsonException) { return null; }
}

async Task WritePipeResultAsync(NamedPipeServerStream pipe, BrokerResult result)
{
    string response = JsonSerializer.Serialize(result);
    await BrokerPipeMessageIO.WriteBoundedLineAsync(pipe, response);
}

BrokerResult MapStoreResult(string requestId, QuarantineStoreResult result) =>
    result.Succeeded
        ? BrokerResult.Ok(requestId, result.ItemId, result.Sha256, result.Message)
        : BrokerResult.Fail(requestId, result.Code, result.Message);

BrokerResult MapServiceRestartResult(string requestId, ServiceRestartResult result) =>
    result.Succeeded
        ? BrokerResult.Ok(requestId, string.Empty, string.Empty, result.Message)
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

        if (!TryGetCurrentPackageFullName(out string brokerPackage))
        {
            error = "The privileged broker has no verifiable Windows package identity and refused the request.";
            return false;
        }
        if (!TryGetProcessPackageFullName(client.Handle, out string clientPackage))
        {
            error = "The IPC client has no verifiable Windows package identity and refused the request.";
            return false;
        }
        if (!BrokerIdentityPolicy.SamePackage(brokerPackage, clientPackage))
        {
            error = "The IPC client package identity does not match the privileged broker package identity.";
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

bool TryGetCurrentPackageFullName(out string packageFullName)
{
    packageFullName = string.Empty;
    uint length = 0;
    int result = GetCurrentPackageFullName(ref length, null);
    if (result != ErrorInsufficientBuffer || length == 0) return false;
    StringBuilder value = new((int)length);
    result = GetCurrentPackageFullName(ref length, value);
    if (result != 0) return false;
    packageFullName = value.ToString();
    return !string.IsNullOrWhiteSpace(packageFullName);
}

bool TryGetProcessPackageFullName(IntPtr processHandle, out string packageFullName)
{
    packageFullName = string.Empty;
    uint length = 0;
    int result = GetPackageFullName(processHandle, ref length, null);
    if (result != ErrorInsufficientBuffer || length == 0) return false;
    StringBuilder value = new((int)length);
    result = GetPackageFullName(processHandle, ref length, value);
    if (result != 0) return false;
    packageFullName = value.ToString();
    return !string.IsNullOrWhiteSpace(packageFullName);
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
    if (req.ProcessId <= 4 || req.ExpectedProcessStartUtcTicks <= 0 || string.IsNullOrWhiteSpace(req.ExpectedImagePath) || string.IsNullOrWhiteSpace(req.ExpectedImageSha256))
        return BrokerResult.Fail(req.RequestId, "IncompleteProcessIdentity", "Exact process identity evidence is required for a non-system process.");

    using Process process = Process.GetProcessById(req.ProcessId);
    long actualStartTicks = process.StartTime.ToUniversalTime().Ticks;
    if (actualStartTicks != req.ExpectedProcessStartUtcTicks)
        return BrokerResult.Fail(req.RequestId, "ProcessIdentityChanged", "The PID now belongs to a different process instance.");

    string actualProcessName = process.ProcessName;
    if (BrokerProcessTerminationPolicy.IsProtected(actualProcessName))
        return BrokerResult.Fail(req.RequestId, "ProtectedProcess", "The privileged broker will not terminate a protected Windows, security, shell, or Sentinel process.");

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

BrokerFirewallRuleVerification QueryFirewallRuleForRemoval(string remoteIp)
{
    string ruleName = BrokerFirewallPolicy.BuildRuleName(remoteIp);
    string command =
        "$name='" + ruleName.Replace("'", "''", StringComparison.Ordinal) + "'; " +
        "$rules=@(Get-NetFirewallRule -PolicyStore ActiveStore -DisplayName $name -ErrorAction SilentlyContinue | Where-Object {$_.DisplayName -eq $name}); " +
        "if($rules.Count -eq 0){'FOUND=0'; exit 0}; if($rules.Count -ne 1){\"FOUND=$($rules.Count)`nCONFLICT=True\"; exit 0}; " +
        "$r=$rules[0]; $a=@($r | Get-NetFirewallAddressFilter); $p=@($r | Get-NetFirewallPortFilter); $app=@($r | Get-NetFirewallApplicationFilter); $svc=@($r | Get-NetFirewallServiceFilter); " +
        "\"FOUND=1`nENABLED=$($r.Enabled)`nACTION=$($r.Action)`nDIRECTION=$($r.Direction)`nPROFILE=$($r.Profile)`nREMOTE=$(@($a.RemoteAddress) -join ',')`nLOCAL=$(@($a.LocalAddress) -join ',')`nPROTOCOL=$($p.Protocol)`nLOCALPORT=$(@($p.LocalPort) -join ',')`nREMOTEPORT=$(@($p.RemotePort) -join ',')`nPROGRAM=$($app.Program)`nSERVICE=$($svc.Service)\"";

    string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
    string powershell = string.IsNullOrWhiteSpace(system)
        ? "powershell.exe"
        : Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
    string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
    ProcessStartInfo startInfo = new()
    {
        FileName = powershell,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-NonInteractive");
    startInfo.ArgumentList.Add("-EncodedCommand");
    startInfo.ArgumentList.Add(encoded);

    BoundedProcessResult result = BoundedProcessRunner.RunAsync(
        startInfo,
        TimeSpan.FromSeconds(12),
        maxOutputChars: 32_000).GetAwaiter().GetResult();
    if (!result.Succeeded)
        return new(false, false, false, $"broker firewall verification failed safely ({result.Code}). {result.Detail}");

    return BrokerFirewallPolicy.EvaluateRemovalEvidence(result.StandardOutput, remoteIp);
}

BrokerResult ApplyFirewallMutation(BrokerRequest req, bool add)
{
    if (!BrokerFirewallPolicy.TryNormalizeRemoteIp(req.RemoteIp, out string remoteIp))
        return BrokerResult.Fail(req.RequestId, "InvalidFirewallTarget", "A literal remote IP address is required for firewall containment.");

    string ruleName = BrokerFirewallPolicy.BuildRuleName(remoteIp);
    if (!add)
    {
        BrokerFirewallRuleVerification verification = QueryFirewallRuleForRemoval(remoteIp);
        if (!verification.QueryValid)
            return BrokerResult.Fail(req.RequestId, "FirewallVerificationFailed", $"The elevated broker could not safely verify the firewall rule immediately before removal ({verification.Detail}). No firewall change was made.");
        if (!verification.Exists)
            return BrokerResult.Ok(req.RequestId, ruleName, string.Empty,
                $"The elevated broker verified that the Sentinel firewall block for {remoteIp} is already absent. No firewall change was needed.");
        if (!verification.IsExactBlock)
            return BrokerResult.Fail(req.RequestId, "FirewallRuleConflict", $"The firewall rule changed before elevated removal or does not exactly match Sentinel's required enabled outbound Block scope ({verification.Detail}). The broker refused to delete it.");
    }

    string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
    string netsh = string.IsNullOrWhiteSpace(system) ? "netsh.exe" : Path.Combine(system, "netsh.exe");
    string[] arguments = add
        ? BrokerFirewallPolicy.BuildAddArguments(remoteIp)
        : BrokerFirewallPolicy.BuildDeleteArguments(remoteIp);

    ProcessStartInfo startInfo = new()
    {
        FileName = netsh,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (string argument in arguments)
        startInfo.ArgumentList.Add(argument);

    BoundedProcessResult result = BoundedProcessRunner.RunAsync(startInfo, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    if (!result.Succeeded)
        return BrokerResult.Fail(req.RequestId, "FirewallMutationFailed", $"Windows Firewall mutation failed safely ({result.Code}). {result.Detail}");

    return BrokerResult.Ok(req.RequestId, ruleName, string.Empty,
        add
            ? $"Windows accepted the exact allowlisted firewall block mutation for {remoteIp}. The desktop client must independently verify active policy before reporting containment success."
            : $"The elevated broker reverified the exact Sentinel firewall rule immediately before removal, Windows accepted the constrained removal mutation for {remoteIp}, and the desktop client must independently verify actual absence before reporting removal success.");
}

void SetAcl(string path, bool usersRead)
{
    string icacls = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "icacls.exe");
    ProcessStartInfo startInfo = new() { FileName = icacls, UseShellExecute = false, CreateNoWindow = true };
    startInfo.ArgumentList.Add(path);
    startInfo.ArgumentList.Add("/inheritance:r");
    startInfo.ArgumentList.Add("/grant:r");
    startInfo.ArgumentList.Add("*S-1-5-18:(OI)(CI)(F)");
    startInfo.ArgumentList.Add("*S-1-5-32-544:(OI)(CI)(F)");
    if (usersRead) startInfo.ArgumentList.Add("*S-1-5-32-545:(OI)(CI)(RX)");
    BoundedProcessResult result = BoundedProcessRunner.RunAsync(startInfo, TimeSpan.FromSeconds(10), maxOutputChars: 32_000).GetAwaiter().GetResult();
    if (!result.Succeeded) throw new InvalidOperationException($"ACL configuration failed safely ({result.Code}).");
}

void SetFileAclPrivate(string path)
{
    string icacls = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "icacls.exe");
    ProcessStartInfo startInfo = new() { FileName = icacls, UseShellExecute = false, CreateNoWindow = true };
    startInfo.ArgumentList.Add(path);
    startInfo.ArgumentList.Add("/inheritance:r");
    startInfo.ArgumentList.Add("/grant:r");
    startInfo.ArgumentList.Add("*S-1-5-18:(F)");
    startInfo.ArgumentList.Add("*S-1-5-32-544:(F)");
    BoundedProcessResult result = BoundedProcessRunner.RunAsync(startInfo, TimeSpan.FromSeconds(10), maxOutputChars: 32_000).GetAwaiter().GetResult();
    if (!result.Succeeded) throw new InvalidOperationException($"Payload ACL configuration failed safely ({result.Code}).");
}

void ResetFileAclInheritance(string path)
{
    string icacls = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "icacls.exe");
    ProcessStartInfo startInfo = new() { FileName = icacls, UseShellExecute = false, CreateNoWindow = true };
    startInfo.ArgumentList.Add(path);
    startInfo.ArgumentList.Add("/reset");
    BoundedProcessResult result = BoundedProcessRunner.RunAsync(startInfo, TimeSpan.FromSeconds(10), maxOutputChars: 32_000).GetAwaiter().GetResult();
    if (!result.Succeeded)
        throw new InvalidOperationException($"Restored file ACL inheritance could not be reset safely ({result.Code}).");
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

[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, StringBuilder? packageFullName);

[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
static extern int GetPackageFullName(IntPtr hProcess, ref uint packageFullNameLength, StringBuilder? packageFullName);

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
    bool TerminateDescendants = false,
    string? RemoteIp = null,
    string? ServiceName = null);

public sealed record BrokerResult(string RequestId, bool Succeeded, string Code, string Message, string ItemId, string Sha256)
{
    public static BrokerResult Ok(string requestId, string itemId, string sha256, string message) => new(requestId, true, "Success", message, itemId, sha256);
    public static BrokerResult Fail(string requestId, string code, string message) => new(requestId, false, code, message, string.Empty, string.Empty);
}
