using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Sentinel.App;

internal static class BootstrapLaunchDiagnostics
{
    private static readonly object Gate = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Write("ManagedModuleLoaded", null);

        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Write("BootstrapAppDomainUnhandledException", args.ExceptionObject as Exception);
        }
        catch
        {
        }
    }

    internal static void Write(string phase, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string directory = Path.Combine(localAppData, "Modern Methods", "Sentinel AI", "Logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "bootstrap-launch.log");

                StringBuilder line = new();
                line.Append(DateTimeOffset.UtcNow.ToString("O"))
                    .Append(" | ").Append(phase)
                    .Append(" | pid=").Append(Environment.ProcessId)
                    .Append(" | framework=").Append(Environment.Version);

                if (exception is not null)
                {
                    line.Append(" | ").Append(exception.GetType().FullName)
                        .Append(": ").Append(Sanitize(exception.Message));
                }

                line.AppendLine();
                File.AppendAllText(path, line.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Launch diagnostics must never affect startup.
        }
    }

    private static string Sanitize(string value) =>
        (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
}
