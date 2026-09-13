using Sentinel.App.Services;
using System.Runtime.CompilerServices;

internal static class ProcessTrustLocationPolicyAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            throw new InvalidOperationException("Windows user profile path was unavailable to the A01 acceptance harness.");

        Require(ProcessTrustLocationPolicy.IsTypicallyUserWritableLocation(
                Path.Combine(userProfile, "Desktop", "unsigned-desktop.exe")),
            "Desktop executable was not treated as a user-writable trust location.");
        Require(ProcessTrustLocationPolicy.IsTypicallyUserWritableLocation(
                Path.Combine(userProfile, "Documents", "unsigned-documents.exe")),
            "Documents executable was not treated as a user-writable trust location.");
        Require(ProcessTrustLocationPolicy.IsTypicallyUserWritableLocation(
                Path.Combine(userProfile, "OneDrive", "Desktop", "unsigned-onedrive.exe")),
            "OneDrive/profile executable was not treated as a user-writable trust location.");
        Require(ProcessTrustLocationPolicy.IsTypicallyUserWritableLocation(
                Path.Combine(userProfile, "Tools", "unsigned-profile-tool.exe")),
            "Arbitrary executable beneath the user profile was not treated as user-writable.");
        Require(ProcessTrustLocationPolicy.IsTypicallyUserWritableLocation(
                Path.Combine(Path.GetTempPath(), "sentinel-a01-temp.exe")),
            "Temporary executable was no longer treated as user-writable.");
        Require(!ProcessTrustLocationPolicy.IsTypicallyUserWritableLocation(string.Empty),
            "Blank executable path was accepted as a trust location.");
        Require(!ProcessTrustLocationPolicy.IsTypicallyUserWritableLocation("relative\\unsigned.exe"),
            "Relative executable path was accepted as a trust location.");

        Console.WriteLine("A01 process trust location coverage: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
