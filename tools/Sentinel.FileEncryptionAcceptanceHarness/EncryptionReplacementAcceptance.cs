using Sentinel.App.Services;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

internal static class EncryptionReplacementAcceptance
{
    [ModuleInitializer]
    internal static void VerifyAtStartup()
    {
        string root = Path.Combine(Path.GetTempPath(), "SentinelEncryptionReplacementAcceptance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        char[] senderPassword = "replace plaintext after verify 2026!".ToCharArray();
        char[] recipientPassword = "replace plaintext after verify 2026!".ToCharArray();
        byte[] original = RandomNumberGenerator.GetBytes(SentinelEncryptedContainerV1.MinimumChunkSize + 91);

        string source = Path.Combine(root, "replace-me.txt");
        string encrypted = source + ".sentinel.senc";
        string restored = Path.Combine(root, "restored.txt");
        File.WriteAllBytes(source, original);

        try
        {
            FileEncryptionService primitive = new(SentinelEncryptedContainerV1.MinimumChunkSize);
            FileEncryptionReplacementService replacement = new(primitive);

            using (PasswordFileKeyProtector sender = new(senderPassword))
            {
                FileEncryptionResult encryption = replacement.EncryptReplacingSourceAsync(
                    source,
                    encrypted,
                    new IFileKeyProtector[] { sender }).GetAwaiter().GetResult();

                Require(encryption.Succeeded && encryption.Verified,
                    "User-facing replacement encryption did not complete: " + encryption.Code + " / " + encryption.Message);
                Require(encryption.Code == "VerifiedAndSourceRetired",
                    "User-facing replacement encryption did not report verified source retirement.");
            }

            Require(!File.Exists(source),
                "User-facing encryption left the readable plaintext source beside the verified encrypted file.");
            Require(File.Exists(encrypted),
                "User-facing replacement encryption did not leave the verified .sentinel.senc file.");

            using (PasswordFileKeyProtector recipient = new(recipientPassword))
            {
                FileDecryptionResult decryption = primitive.DecryptAsync(
                    encrypted,
                    restored,
                    new IFileKeyProtector[] { recipient }).GetAwaiter().GetResult();
                Require(decryption.Succeeded,
                    "Replacement encrypted output could not be decrypted after source retirement: " + decryption.Code);
            }

            Require(File.Exists(restored), "Replacement round trip did not restore plaintext.");
            Require(File.ReadAllBytes(restored).AsSpan().SequenceEqual(original),
                "Replacement round trip did not restore the exact original bytes.");
        }
        finally
        {
            Array.Clear(senderPassword, 0, senderPassword.Length);
            Array.Clear(recipientPassword, 0, recipientPassword.Length);
            CryptographicOperations.ZeroMemory(original);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
