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
// Keep the exception deliberately narrow: the runner itself must own Process directly.
Regex directProcessOwnership = new(
    @"\bProcess\s*\.\s*Start\s*\(|\bnew\s+Process\s*\(|\bnew\s+Process\s*\{|\bProcess\s+\w+\s*=\s*new\s*\(",
    RegexOptions.CultureInvariant | RegexOptions.Compiled);

List<string> violations = new();
foreach (string file in Directory.EnumerateFiles(productionRoot, "*.cs", SearchOption.AllDirectories))
{
    string name = Path.GetFileName(file);
    if (name.Equals("BoundedProcessRunner.cs", StringComparison.OrdinalIgnoreCase))
        continue;

    string text = File.ReadAllText(file);
    if (directProcessOwnership.IsMatch(text))
        violations.Add(Path.GetRelativePath(root, file));
}

if (violations.Count > 0)
{
    StringBuilder message = new();
    message.AppendLine("Direct subprocess ownership remains outside BoundedProcessRunner:");
    foreach (string violation in violations.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        message.AppendLine(" - " + violation);
    throw new InvalidOperationException(message.ToString());
}

Console.WriteLine("Subprocess boundary source audit PASS");
