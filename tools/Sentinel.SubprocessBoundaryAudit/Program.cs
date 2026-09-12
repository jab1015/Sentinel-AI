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

foreach (string allowedRunnerPath in allowedRunnerPaths)
{
    if (!File.Exists(allowedRunnerPath))
        throw new InvalidOperationException($"Expected bounded subprocess runner was not found: {Path.GetRelativePath(root, allowedRunnerPath)}");
}
if (!File.Exists(auditedShellLaunchPath))
    throw new InvalidOperationException("Expected audited Windows shell-launch service was not found.");

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
        if (initializer.Success)
        {
            HashSet<string> actualTargets = Regex.Matches(
                    initializer.Groups["body"].Value,
                    "\"(?<value>(?:\\.|[^\"\\])*)\"",
                    RegexOptions.CultureInvariant)
                .Select(match => Regex.Unescape(match.Groups["value"].Value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool exactTargetSet = actualTargets.SetEquals(expectedShellTargets) &&
                                  actualTargets.Count == expectedShellTargets.Count;
            bool failClosedInput = text.Contains("string.IsNullOrWhiteSpace(target) || !AllowedTargets.Contains(target)", StringComparison.Ordinal);
            bool shellOnly = text.Contains("UseShellExecute = true", StringComparison.Ordinal);
            bool directStart = text.Contains("Process.Start(new ProcessStartInfo", StringComparison.Ordinal);

            if (exactTargetSet && failClosedInput && shellOnly && directStart)
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