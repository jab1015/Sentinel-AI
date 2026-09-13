using System.Text;
using System.Text.RegularExpressions;

static string FindRepoRoot()
{
    DirectoryInfo? current = new(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
            File.Exists(Path.Combine(current.FullName, "Sentinel-AI.sln")) ||
            Directory.Exists(Path.Combine(current.FullName, "src", "SentinelAI")))
            return current.FullName;
        current = current.Parent;
    }
    throw new InvalidOperationException("Repository root could not be located.");
}

static bool TryParseQuotedInitializerValues(string body, out HashSet<string> values)
{
    values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string rawLine in body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
    {
        string line = rawLine.Trim();
        if (line.Length == 0) continue;
        if (line.EndsWith(",", StringComparison.Ordinal))
            line = line[..^1].TrimEnd();
        if (line.Length < 2 || line[0] != '"' || line[^1] != '"')
            return false;

        string value = line[1..^1];
        if (value.Contains('\\') || value.Contains('"'))
            return false;
        if (!values.Add(value))
            return false;
    }
    return values.Count > 0;
}

string root = FindRepoRoot();
string productionRoot = Path.Combine(root, "src", "SentinelAI");
if (!Directory.Exists(productionRoot)) throw new InvalidOperationException("Production source directory was not found.");

Regex directProcessOwnership = new(
    @"\bProcess\s*\.\s*Start\s*\(|\bnew\s+(?:System\.Diagnostics\.)?Process\s*\(|\bnew\s+(?:System\.Diagnostics\.)?Process\s*\{|\bProcess\s+\w+\s*=\s*new\s*\(",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

HashSet<string> allowedRunnerPaths = new(StringComparer.OrdinalIgnoreCase)
{
    Path.GetFullPath(Path.Combine(root, "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "Services", "BoundedProcessRunner.cs")),
    Path.GetFullPath(Path.Combine(root, "src", "SentinelAI", "Sentinel.PrivilegedBroker", "BoundedProcessRunner.cs"))
};

string auditedShellLaunchPath = Path.GetFullPath(Path.Combine(
    root, "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "Services", "WindowsShellLaunchService.cs"));
string auditedRestartRequestPath = Path.GetFullPath(Path.Combine(
    root, "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "Services", "WindowsRestartRequestService.cs"));

foreach (string allowedRunnerPath in allowedRunnerPaths)
{
    if (!File.Exists(allowedRunnerPath))
        throw new InvalidOperationException($"Expected bounded subprocess runner was not found: {Path.GetRelativePath(root, allowedRunnerPath)}");
}
if (!File.Exists(auditedShellLaunchPath))
    throw new InvalidOperationException("Expected audited Windows shell-launch service was not found.");
if (!File.Exists(auditedRestartRequestPath))
    throw new InvalidOperationException("Expected audited Windows restart-request service was not found.");

string restartRequestText = File.ReadAllText(auditedRestartRequestPath);
string[] requiredRestartMarkers =
{
    "Path.Combine(Environment.SystemDirectory, \"shutdown.exe\")",
    "Path.IsPathFullyQualified(shutdownPath)",
    "File.Exists(shutdownPath)",
    "FileName = shutdownPath",
    "Arguments = \"/r /t 0\"",
    "UseShellExecute = false",
    "CreateNoWindow = true",
    "BoundedProcessRunner.RunAsync",
    "RestartRequestTimeout",
    "maxOutputChars: 16_384"
};
if (requiredRestartMarkers.Any(marker => !restartRequestText.Contains(marker, StringComparison.Ordinal)) ||
    restartRequestText.Contains("FileName = \"shutdown.exe\"", StringComparison.Ordinal))
{
    throw new InvalidOperationException("The audited Windows restart-request contract changed or no longer pins shutdown.exe to the trusted Windows system directory.");
}

HashSet<string> expectedShellTargets = new(StringComparer.OrdinalIgnoreCase)
{
    "taskmgr.exe",
    "ms-settings:windowsupdate",
    "windowsdefender:",
    "windowsdefender://network",
    "services.msc",
    "ms-settings:storagesense"
};

List<string> violations = new();
foreach (string file in Directory.EnumerateFiles(productionRoot, "*.cs", SearchOption.AllDirectories))
{
    string fullFile = Path.GetFullPath(file);
    if (allowedRunnerPaths.Contains(fullFile))
        continue;

    string text = File.ReadAllText(file);
    if (!directProcessOwnership.IsMatch(text))
        continue;

    string relative = Path.GetRelativePath(root, file);

    if (fullFile.Equals(auditedShellLaunchPath, StringComparison.OrdinalIgnoreCase))
    {
        Match initializer = Regex.Match(
            text,
            @"AllowedTargets\s*=\s*new\s*\([^)]*\)\s*\{(?<body>.*?)\};",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (initializer.Success &&
            TryParseQuotedInitializerValues(initializer.Groups["body"].Value, out HashSet<string> actualTargets))
        {
            bool exactTargetSet = actualTargets.SetEquals(expectedShellTargets) &&
                                  actualTargets.Count == expectedShellTargets.Count;
            bool failClosedInput = text.Contains("string.IsNullOrWhiteSpace(target) || !AllowedTargets.Contains(target)", StringComparison.Ordinal);
            bool trustedFileResolution =
                text.Contains("TryResolveSystemFile(\"Taskmgr.exe\"", StringComparison.Ordinal) &&
                text.Contains("TryResolveSystemFile(\"services.msc\"", StringComparison.Ordinal) &&
                text.Contains("Path.Combine(Environment.SystemDirectory, fileName)", StringComparison.Ordinal) &&
                text.Contains("Path.IsPathFullyQualified(resolvedPath) && File.Exists(resolvedPath)", StringComparison.Ordinal);
            bool resolvedLaunch = text.Contains("FileName = resolvedTarget", StringComparison.Ordinal);
            bool shellOnly = text.Contains("UseShellExecute = true", StringComparison.Ordinal);
            bool directStart = text.Contains("Process.Start(new ProcessStartInfo", StringComparison.Ordinal);
            bool noBareFileLaunch = !text.Contains("FileName = target", StringComparison.Ordinal);

            if (exactTargetSet && failClosedInput && trustedFileResolution && resolvedLaunch && shellOnly && directStart && noBareFileLaunch)
                continue;
        }

        violations.Add(relative + " (audited shell-launch contract changed)");
        continue;
    }

    if (relative.EndsWith(Path.Combine("Services", "PrivilegedBrokerClient.cs"), StringComparison.OrdinalIgnoreCase))
    {
        string[] requiredSafetyMarkers =
        {
            "UseShellExecute = true",
            "Verb = \"runas\"",
            "GetNamedPipeServerProcessId",
            "connectedPid != process.Id",
            "CancellationTokenSource.CreateLinkedTokenSource(token)",
            "operationDeadline.CancelAfter(timeout)",
            "pipe.ConnectAsync(operationToken).WaitAsync(TimeSpan.FromSeconds(20), operationToken)",
            "writer.WriteLineAsync(requestJson).WaitAsync(operationToken)",
            "ReadBoundedUtf8LineAsync(pipe, MaximumRequestBytes, operationToken)",
            "line.Length + segmentLength > maximumBytes",
            "StrictUtf8.GetString",
            "process.WaitForExitAsync(operationToken)",
            "process.ExitCode != 0",
            "BrokerExitFailure",
            "did not accept the operation as successful",
            "catch (OperationCanceledException) when (token.IsCancellationRequested)",
            "bool brokerExitVerified = TerminateBroker(process)",
            "could not verify the broker exited",
            "process.Kill(entireProcessTree: true)",
            "process.WaitForExit(5_000)",
            "return process.HasExited"
        };
        if (requiredSafetyMarkers.All(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            if (!text.Contains("WriteLineAsync(requestJson).WaitAsync(timeout", StringComparison.Ordinal) &&
                !text.Contains("ReadLineAsync().WaitAsync(timeout", StringComparison.Ordinal) &&
                !text.Contains("WaitForExitAsync(token).WaitAsync(timeout", StringComparison.Ordinal) &&
                !text.Contains("Sentinel terminated the broker", StringComparison.OrdinalIgnoreCase))
                continue;
        }
    }

    violations.Add(relative);
}

if (violations.Count > 0)
{
    StringBuilder message = new();
    message.AppendLine("Direct subprocess ownership remains outside an audited bounded runner or exact shell-launch owner:");
    foreach (string violation in violations.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        message.AppendLine(" - " + violation);
    throw new InvalidOperationException(message.ToString());
}

Console.WriteLine("Subprocess boundary source audit PASS");