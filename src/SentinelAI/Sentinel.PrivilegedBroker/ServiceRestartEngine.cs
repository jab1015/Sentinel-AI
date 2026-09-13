using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

internal sealed class ServiceRestartEngine
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceEnumerateDependents = 0x0008;
    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceActive = 0x00000001;
    private const int ScStatusProcessInfo = 0;
    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceStopPending = 0x00000003;
    private const uint ServiceRunning = 0x00000004;
    private const int ErrorMoreData = 234;
    private const int MaximumServiceNameLength = 128;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private static readonly HashSet<string> AllowedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "BITS",
        "wuauserv",
        "Spooler"
    };

    private readonly string _transactionRoot;

    internal ServiceRestartEngine(string brokerTransactionRoot)
    {
        if (string.IsNullOrWhiteSpace(brokerTransactionRoot)) throw new ArgumentNullException(nameof(brokerTransactionRoot));
        _transactionRoot = Path.Combine(brokerTransactionRoot, "ServiceRestarts");
    }

    internal ServiceRestartResult RecoverPending()
    {
        try
        {
            if (!Directory.Exists(_transactionRoot))
                return ServiceRestartResult.Ok("No service-restart recovery was required.");

            foreach (string path in Directory.EnumerateFiles(_transactionRoot, "service-restart-*.json", SearchOption.TopDirectoryOnly))
            {
                ServiceRestartTransaction? transaction = ReadTransaction(path);
                if (transaction is null ||
                    !AllowedServices.Contains(transaction.ServiceName) ||
                    !Guid.TryParseExact(transaction.TransactionId, "N", out _))
                {
                    return ServiceRestartResult.Fail("RecoveryRecordInvalid", "A pending service-restart recovery record could not be validated. Service restart remains blocked.");
                }

                if (!transaction.OriginallyRunning)
                    return ServiceRestartResult.Fail("RecoveryRecordInvalid", "A pending service-restart record did not preserve a running-service state. Service restart remains blocked.");

                ServiceRestartResult recovery = EnsureRunning(transaction.ServiceName);
                if (!recovery.Succeeded)
                    return ServiceRestartResult.Fail("RecoveryFailed", $"Sentinel could not restore {transaction.ServiceName} to its recorded running state. {recovery.Message}");

                DeleteTransaction(path);
            }

            return ServiceRestartResult.Ok("Pending service-restart recovery completed.");
        }
        catch (Exception ex)
        {
            return ServiceRestartResult.Fail("RecoveryFailed", $"Service-restart recovery failed safely ({ex.GetType().Name}).");
        }
    }

    internal ServiceRestartResult Restart(string? serviceName)
    {
        if (!TryNormalizeServiceName(serviceName, out string normalized))
            return ServiceRestartResult.Fail("ServiceNotAllowlisted", "The requested Windows service is not in Sentinel's privileged restart allowlist.");

        ServiceRestartResult recovery = RecoverPending();
        if (!recovery.Succeeded) return recovery;

        IntPtr scm = IntPtr.Zero;
        IntPtr service = IntPtr.Zero;
        string? transactionPath = null;
        try
        {
            scm = OpenSCManagerW(null, null, ScManagerConnect);
            if (scm == IntPtr.Zero)
                return Win32Failure("ScmOpenFailed", "Windows Service Control Manager could not be opened.");

            service = OpenServiceW(scm, normalized, ServiceQueryStatus | ServiceStart | ServiceStop | ServiceEnumerateDependents);
            if (service == IntPtr.Zero)
                return Win32Failure("ServiceOpenFailed", "The exact allowlisted Windows service could not be opened.");

            if (!TryQueryStatus(service, out SERVICE_STATUS_PROCESS status))
                return Win32Failure("ServiceStatusUnverified", "Sentinel could not verify the current service state.");
            if (status.dwCurrentState != ServiceRunning)
                return ServiceRestartResult.Fail("ServiceNotRunning", "Sentinel only restarts an allowlisted service that is verified running; no state change was made.");

            string[] activeDependents = GetActiveDependentServices(service);
            if (activeDependents.Length > 0)
                return ServiceRestartResult.Fail("ActiveDependents", $"Sentinel refused to restart {normalized} because active dependent services were detected: {string.Join(", ", activeDependents.Take(8))}.");

            ServiceRestartTransaction transaction = new(Guid.NewGuid().ToString("N"), normalized, true, DateTimeOffset.UtcNow);
            transactionPath = PersistTransaction(transaction);
            if (transactionPath is null)
                return ServiceRestartResult.Fail("ReservationFailed", "Sentinel could not durably reserve rollback state before stopping the service. No state change was made.");

            if (!ControlService(service, ServiceControlStop, out _))
            {
                int error = Marshal.GetLastWin32Error();
                ServiceRestartResult rollback = EnsureRunningHandle(service, normalized);
                if (rollback.Succeeded) DeleteTransaction(transactionPath);
                return Win32FailureWithRollback("StopFailed", "Windows rejected the verified service stop request.", error, rollback);
            }

            if (!WaitForState(service, ServiceStopped, StopTimeout))
            {
                ServiceRestartResult rollback = EnsureRunningHandle(service, normalized);
                if (rollback.Succeeded) DeleteTransaction(transactionPath);
                return ServiceRestartResult.Fail(rollback.Succeeded ? "StopTimeoutRolledBack" : "StopTimeoutRollbackPending",
                    rollback.Succeeded ? "The service did not reach Stopped in time; Sentinel restored and verified its original running state." : "The service did not reach Stopped in time and Sentinel could not verify restoration. Durable recovery remains pending.");
            }

            if (!StartServiceW(service, 0, null))
            {
                int error = Marshal.GetLastWin32Error();
                ServiceRestartResult rollback = EnsureRunningHandle(service, normalized);
                if (rollback.Succeeded) DeleteTransaction(transactionPath);
                return Win32FailureWithRollback("StartFailed", "Windows rejected the service restart request after stop.", error, rollback);
            }

            if (!WaitForState(service, ServiceRunning, StartTimeout))
            {
                ServiceRestartResult rollback = EnsureRunningHandle(service, normalized);
                if (rollback.Succeeded) DeleteTransaction(transactionPath);
                return ServiceRestartResult.Fail(rollback.Succeeded ? "StartTimeoutRolledBack" : "StartTimeoutRollbackPending",
                    rollback.Succeeded ? "The service restart did not verify in time; Sentinel restored and verified the original running state." : "The service restart did not verify and the original running state could not be restored. Durable recovery remains pending.");
            }

            DeleteTransaction(transactionPath);
            transactionPath = null;
            return ServiceRestartResult.Ok($"Sentinel restarted {normalized} and independently verified that it is running.");
        }
        catch (Exception ex)
        {
            if (transactionPath is not null)
            {
                ServiceRestartResult rollback = EnsureRunning(normalized);
                if (rollback.Succeeded) DeleteTransaction(transactionPath);
                return ServiceRestartResult.Fail(rollback.Succeeded ? "RestartFailedRolledBack" : "RestartFailedRollbackPending",
                    rollback.Succeeded ? $"Service restart failed safely ({ex.GetType().Name}); Sentinel restored and verified the original running state." : $"Service restart failed ({ex.GetType().Name}) and restoration could not be verified. Durable recovery remains pending.");
            }
            return ServiceRestartResult.Fail("RestartFailed", $"Service restart failed safely before a verified state change ({ex.GetType().Name}).");
        }
        finally
        {
            if (service != IntPtr.Zero) CloseServiceHandle(service);
            if (scm != IntPtr.Zero) CloseServiceHandle(scm);
        }
    }

    internal static bool TryNormalizeServiceName(string? serviceName, out string normalized)
    {
        normalized = (serviceName ?? string.Empty).Trim();
        if (normalized.Length is <= 0 or > MaximumServiceNameLength || !AllowedServices.Contains(normalized))
        {
            normalized = string.Empty;
            return false;
        }
        return true;
    }

    private ServiceRestartResult EnsureRunning(string serviceName)
    {
        IntPtr scm = IntPtr.Zero;
        IntPtr service = IntPtr.Zero;
        try
        {
            scm = OpenSCManagerW(null, null, ScManagerConnect);
            if (scm == IntPtr.Zero) return Win32Failure("ScmOpenFailed", "Recovery could not open Service Control Manager.");
            service = OpenServiceW(scm, serviceName, ServiceQueryStatus | ServiceStart);
            if (service == IntPtr.Zero) return Win32Failure("ServiceOpenFailed", "Recovery could not open the allowlisted service.");
            return EnsureRunningHandle(service, serviceName);
        }
        finally
        {
            if (service != IntPtr.Zero) CloseServiceHandle(service);
            if (scm != IntPtr.Zero) CloseServiceHandle(scm);
        }
    }

    private static ServiceRestartResult EnsureRunningHandle(IntPtr service, string serviceName)
    {
        if (!TryQueryStatus(service, out SERVICE_STATUS_PROCESS status))
            return ServiceRestartResult.Fail("RollbackStateUnknown", "The service state could not be verified for rollback.");
        if (status.dwCurrentState == ServiceRunning)
            return ServiceRestartResult.Ok($"{serviceName} is running.");
        if (status.dwCurrentState == ServiceStopPending && !WaitForState(service, ServiceStopped, StopTimeout))
            return ServiceRestartResult.Fail("RollbackStopPending", "The service remained stop-pending and could not be safely restored.");
        if (!StartServiceW(service, 0, null))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 1056)
                return ServiceRestartResult.Fail("RollbackStartFailed", $"Windows could not restore the service (Win32 {error}).");
        }
        return WaitForState(service, ServiceRunning, StartTimeout)
            ? ServiceRestartResult.Ok($"{serviceName} was restored to Running.")
            : ServiceRestartResult.Fail("RollbackUnverified", "The original running state could not be verified within the recovery window.");
    }

    private string? PersistTransaction(ServiceRestartTransaction transaction)
    {
        try
        {
            Directory.CreateDirectory(_transactionRoot);
            string finalPath = Path.Combine(_transactionRoot, $"service-restart-{transaction.TransactionId}.json");
            string tempPath = finalPath + ".tmp";
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(transaction);
            using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(true);
            }
            File.Move(tempPath, finalPath, false);
            ServiceRestartTransaction? verified = ReadTransaction(finalPath);
            if (verified != transaction)
            {
                try { File.Delete(finalPath); } catch { }
                return null;
            }
            return finalPath;
        }
        catch { return null; }
    }

    private static ServiceRestartTransaction? ReadTransaction(string path)
    {
        try
        {
            FileInfo info = new(path);
            if (info.Length is <= 0 or > 16_384) return null;
            return JsonSerializer.Deserialize<ServiceRestartTransaction>(File.ReadAllBytes(path));
        }
        catch { return null; }
    }

    private static void DeleteTransaction(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static string[] GetActiveDependentServices(IntPtr service)
    {
        if (EnumDependentServicesW(service, ServiceActive, IntPtr.Zero, 0, out uint bytesNeeded, out uint count))
            return Array.Empty<string>();
        int error = Marshal.GetLastWin32Error();
        if (error != ErrorMoreData || bytesNeeded == 0)
            throw new Win32Exception(error, "Active dependent-service state could not be verified.");

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)bytesNeeded));
        try
        {
            if (!EnumDependentServicesW(service, ServiceActive, buffer, bytesNeeded, out _, out count))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Active dependent-service state could not be verified.");
            int size = Marshal.SizeOf<ENUM_SERVICE_STATUS>();
            string[] names = new string[checked((int)count)];
            for (int i = 0; i < names.Length; i++)
            {
                ENUM_SERVICE_STATUS entry = Marshal.PtrToStructure<ENUM_SERVICE_STATUS>(IntPtr.Add(buffer, checked(i * size)));
                names[i] = Marshal.PtrToStringUni(entry.lpServiceName) ?? "unknown";
            }
            return names;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool WaitForState(IntPtr service, uint desiredState, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!TryQueryStatus(service, out SERVICE_STATUS_PROCESS status)) return false;
            if (status.dwCurrentState == desiredState) return true;
            Thread.Sleep(250);
        }
        return TryQueryStatus(service, out SERVICE_STATUS_PROCESS finalStatus) && finalStatus.dwCurrentState == desiredState;
    }

    private static bool TryQueryStatus(IntPtr service, out SERVICE_STATUS_PROCESS status)
    {
        status = default;
        int size = Marshal.SizeOf<SERVICE_STATUS_PROCESS>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryServiceStatusEx(service, ScStatusProcessInfo, buffer, (uint)size, out _)) return false;
            status = Marshal.PtrToStructure<SERVICE_STATUS_PROCESS>(buffer);
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static ServiceRestartResult Win32Failure(string code, string message) =>
        ServiceRestartResult.Fail(code, $"{message} (Win32 {Marshal.GetLastWin32Error()}).");

    private static ServiceRestartResult Win32FailureWithRollback(string code, string message, int error, ServiceRestartResult rollback) =>
        ServiceRestartResult.Fail(rollback.Succeeded ? code + "RolledBack" : code + "RollbackPending",
            rollback.Succeeded ? $"{message} (Win32 {error}) Sentinel restored and verified the original running state." : $"{message} (Win32 {error}) The original running state could not be verified; durable recovery remains pending.");

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint, dwProcessId, dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ENUM_SERVICE_STATUS
    {
        public IntPtr lpServiceName;
        public IntPtr lpDisplayName;
        public SERVICE_STATUS ServiceStatus;
    }

    private sealed record ServiceRestartTransaction(string TransactionId, string ServiceName, bool OriginallyRunning, DateTimeOffset CreatedUtc);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr scm, string serviceName, uint desiredAccess);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, IntPtr buffer, uint bufferSize, out uint bytesNeeded);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDependentServicesW(IntPtr service, uint serviceState, IntPtr services, uint bufferSize, out uint bytesNeeded, out uint servicesReturned);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr service, uint control, out SERVICE_STATUS status);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceW(IntPtr service, uint numServiceArgs, string[]? serviceArgVectors);
}

internal sealed record ServiceRestartResult(bool Succeeded, string Code, string Message)
{
    internal static ServiceRestartResult Ok(string message) => new(true, "Success", message);
    internal static ServiceRestartResult Fail(string code, string message) => new(false, code, message);
}
