using System.Text;

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
string services = Path.Combine(root, "src", "SentinelAI", "Sentinel.App", "Sentinel.App", "Services");
if (!Directory.Exists(services)) throw new InvalidOperationException("Services directory was not found.");

List<string> violations = new();
foreach (string file in Directory.EnumerateFiles(services, "*.cs", SearchOption.AllDirectories))
{
    string name = Path.GetFileName(file);
    if (name.Equals("BoundedProcessRunner.cs", StringComparison.OrdinalIgnoreCase))
        continue;

    string text = File.ReadAllText(file);
    bool redirects = text.Contains("RedirectStandardOutput", StringComparison.Ordinal) ||
                     text.Contains("RedirectStandardError", StringComparison.Ordinal) ||
                     text.Contains("StandardOutput.Read", StringComparison.Ordinal) ||
                     text.Contains("StandardError.Read", StringComparison.Ordinal) ||
                     text.Contains("BeginOutputReadLine", StringComparison.Ordinal) ||
                     text.Contains("BeginErrorReadLine", StringComparison.Ordinal);
    bool ownsProcess = text.Contains("new Process", StringComparison.Ordinal) ||
                       text.Contains("Process process", StringComparison.Ordinal) ||
                       text.Contains("Process.Start(", StringComparison.Ordinal);

    if (redirects && ownsProcess)
        violations.Add(Path.GetRelativePath(root, file));
}

if (violations.Count > 0)
{
    StringBuilder message = new();
    message.AppendLine("Direct redirected subprocess ownership remains outside BoundedProcessRunner:");
    foreach (string violation in violations.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        message.AppendLine(" - " + violation);
    throw new InvalidOperationException(message.ToString());
}

Console.WriteLine("Subprocess boundary source audit PASS");
