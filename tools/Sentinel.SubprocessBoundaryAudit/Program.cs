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

// A17 is a production-wide boundary, not a Services-directory convention. Any code that
// directly creates or starts a System.Diagnostics.Process can bypass the common timeout,
// cancellation, process-tree termination, concurrent drain, and output-bound guarantees.
Regex directProcessOwnership = new(
    @"\bProcess\s*\.\s*Start\s*\(|\bnew\s+(?:System\.Diagnostics\.)?Process\s*\(|\bnew\s+(?:System\.Diagnostics\.)?Process\s*\{|\bProcess\s+\w+\s*=\s*new\s*\(",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

HashSet<string> allowedRunnerPaths = new(StringComparer.OrdinalIgnoreCase)
{
    Path.GetFullPath(Path.Combine(root, "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "Services", "BoundedProcessRunner.cs")),
    Path.GetFullPath(Path.Combine(root, "src", "SentinelAI", "Sentinel.PrivilegedBroker", "BoundedProcessRunner.cs"))
};

foreach (string allowedRunnerPath in allowedRunnerPaths)
{
    if (!File.Exists(allowedRunnerPath))
        throw new InvalidOperationException($"Expected bounded subprocess runner was not found: {Path.GetRelativePath(root, allowedRunnerPath)}");
}

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
    if (relative.EndsWith(Path.Combine("Services", "PrivilegedBrokerClient.cs"), StringComparison.OrdinalIgnoreCase))
    {
        // UAC elevation requires ShellExecute/runas and therefore cannot use the redirected-output
        // runner. This is the one production exception. It must retain authenticated PID-bound IPC,
        // one linked wall-clock deadline across connect/write/read/exit, and bounded tree termination.
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
            "reader.ReadLineAsync().WaitAsync(operationToken)",
            "process.WaitForExitAsync(operationToken)",
            "catch (OperationCanceledException) when (token.IsCancellationRequested)",
            "TerminateBroker(process)",
            "process.Kill(entireProcessTree: true)",
            "process.WaitForExit(5_000)"
        };
        if (requiredSafetyMarkers.All(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            // Fresh per-stage use of the full operation timeout would allow cumulative timeout windows.
            // The UAC exception is accepted only when write/read do not reset WaitAsync(timeout, ...).
            if (!text.Contains("WriteLineAsync(requestJson).WaitAsync(timeout", StringComparison.Ordinal) &&
                !text.Contains("ReadLineAsync().WaitAsync(timeout", StringComparison.Ordinal) &&
                !text.Contains("WaitForExitAsync(token).WaitAsync(timeout", StringComparison.Ordinal))
                continue;
        }
    }

    violations.Add(relative);
}

if (violations.Count > 0)
{
    StringBuilder message = new();
    message.AppendLine("Direct subprocess ownership remains outside an audited bounded runner:");
    foreach (string violation in violations.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        message.AppendLine(" - " + violation);
    throw new InvalidOperationException(message.ToString());
}

Console.WriteLine("Subprocess boundary source audit PASS");