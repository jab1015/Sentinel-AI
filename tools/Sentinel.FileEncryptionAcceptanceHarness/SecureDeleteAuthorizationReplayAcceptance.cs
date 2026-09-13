using Sentinel.App.Services;
using System.Runtime.CompilerServices;

internal static class SecureDeleteAuthorizationReplayAcceptance
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    private static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelSecureDeleteReplay", Guid.NewGuid().ToString("N"));
        string journalRoot = Path.Combine(root, "journal");
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "replay.txt");
            File.WriteAllText(path, "authorization replay fixture");
            SecureDeleteTargetValidationResult validation = SecureDeleteTargetValidator.Validate(path);
            Require(validation.Succeeded, "Replay fixture validation failed.");

            SecureDeleteCoordinator coordinator = new();
            SecureDeletePreparationResult prepared = coordinator.Prepare(validation.Target);
            Require(prepared.Succeeded && prepared.Authorization is not null, "Replay fixture preparation failed.");

            SecureDeleteOperationJournal firstProcess = new(journalRoot);
            SecureDeleteOperationRecord first = firstProcess.Begin(prepared.Authorization!);
            Require(first.State == SecureDeleteOperationState.Prepared, "First authorization claim was not durably prepared.");

            // Reopen the journal to model a process restart. The same authorization must stay
            // consumed even though the exact original file still exists and no mutation ran.
            SecureDeleteOperationJournal restartedProcess = new(journalRoot);
            bool replayRejected = false;
            try
            {
                _ = restartedProcess.Begin(prepared.Authorization!);
            }
            catch (InvalidOperationException ex)
            {
                replayRejected = ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase) ||
                                 ex.Message.Contains("replay", StringComparison.OrdinalIgnoreCase);
            }

            Require(replayRejected, "A Secure Delete authorization could be replayed after journal restart.");
            Require(File.Exists(path) && File.ReadAllText(path) == "authorization replay fixture",
                "Authorization replay testing unexpectedly mutated the target.");
            Require(Directory.EnumerateFiles(journalRoot, "authorization-*.claim").Count() == 1,
                "Durable authorization claim was not retained exactly once.");

            Console.WriteLine("Secure Delete durable authorization replay rejection: PASS");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
