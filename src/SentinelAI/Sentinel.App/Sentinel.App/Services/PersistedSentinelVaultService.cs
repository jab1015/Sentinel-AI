using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.App.Services;

/// <summary>
/// Persists only the authenticated wrapped Vault master-key envelope. The plaintext VMK is
/// never written to disk. New vaults are protected by Windows current-user DPAPI plus an
/// independent recovery key that must be shown/saved by the user before creation proceeds.
/// </summary>
internal sealed class PersistedSentinelVaultService
{
    private const string EnvelopeFileName = "vault-master-key-envelope.json";
    private readonly string _root;
    private readonly string _envelopePath;

    internal PersistedSentinelVaultService(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A vault root is required.", nameof(root));
        _root = Path.GetFullPath(root);
        _envelopePath = Path.Combine(_root, EnvelopeFileName);
    }

    internal bool Exists => File.Exists(_envelopePath);

    internal async Task<PersistedVaultSessionResult> CreateAsync(
        RecoveryKeyMaterial recoveryKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recoveryKey);
        Directory.CreateDirectory(_root);
        if (File.Exists(_envelopePath) || Directory.Exists(_envelopePath))
            return PersistedVaultSessionResult.Fail("AlreadyExists", "A Sentinel Vault already exists for this user.");

        SentinelVaultService vault = new();
        try
        {
            WindowsCurrentUserFileKeyProtector windows = new();
            using RecoveryKeyFileKeyProtector recovery = recoveryKey.CreateProtector();
            VaultMasterKeyEnvelope envelope = await vault.InitializeNewVaultAsync(
                new IFileKeyProtector[] { windows, recovery },
                cancellationToken).ConfigureAwait(false);

            await PersistEnvelopeNewAsync(envelope, cancellationToken).ConfigureAwait(false);
            VaultItemStoreService items = new(_root, vault);
            return PersistedVaultSessionResult.Success(new PersistedVaultSession(vault, items));
        }
        catch (OperationCanceledException)
        {
            vault.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException)
        {
            vault.Dispose();
            return PersistedVaultSessionResult.Fail("CreateFailed", "Sentinel could not create and persist the Vault safely.");
        }
    }

    internal async Task<PersistedVaultSessionResult> OpenCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_envelopePath))
            return PersistedVaultSessionResult.Fail("NotFound", "No Sentinel Vault exists for this user.");

        VaultMasterKeyEnvelope? envelope;
        try
        {
            await using FileStream stream = new(_envelopePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > 256 * 1024)
                return PersistedVaultSessionResult.Fail("EnvelopeInvalid", "The persisted Vault envelope is empty or oversized.");
            envelope = await JsonSerializer.DeserializeAsync<VaultMasterKeyEnvelope>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return PersistedVaultSessionResult.Fail("EnvelopeUnavailable", "The persisted Vault envelope could not be read safely.");
        }

        if (envelope is null)
            return PersistedVaultSessionResult.Fail("EnvelopeInvalid", "The persisted Vault envelope was invalid.");

        SentinelVaultService vault = new();
        try
        {
            WindowsCurrentUserFileKeyProtector windows = new();
            VaultUnlockResult unlock = await vault.UnlockAsync(
                envelope,
                new IFileKeyProtector[] { windows },
                cancellationToken).ConfigureAwait(false);
            if (!unlock.Succeeded)
            {
                vault.Dispose();
                return PersistedVaultSessionResult.Fail(unlock.Code, "The current Windows user could not unlock this Sentinel Vault. Use the recovery workflow instead of overwriting it.");
            }

            VaultItemStoreService items = new(_root, vault);
            return PersistedVaultSessionResult.Success(new PersistedVaultSession(vault, items));
        }
        catch (OperationCanceledException)
        {
            vault.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
        {
            vault.Dispose();
            return PersistedVaultSessionResult.Fail("UnlockFailed", "Sentinel could not unlock the Vault safely.");
        }
    }

    private async Task PersistEnvelopeNewAsync(VaultMasterKeyEnvelope envelope, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        string temp = _envelopePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, envelope, cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, _envelopePath, overwrite: false);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}

internal sealed class PersistedVaultSession : IDisposable
{
    internal PersistedVaultSession(SentinelVaultService vault, VaultItemStoreService items)
    {
        Vault = vault;
        Items = items;
    }

    internal SentinelVaultService Vault { get; }
    internal VaultItemStoreService Items { get; }

    public void Dispose() => Vault.Dispose();
}

internal sealed record PersistedVaultSessionResult(bool Succeeded, string Code, string Message, PersistedVaultSession? Session)
{
    internal static PersistedVaultSessionResult Success(PersistedVaultSession session) =>
        new(true, "Opened", "Sentinel Vault session is unlocked.", session);
    internal static PersistedVaultSessionResult Fail(string code, string message) =>
        new(false, code, message, null);
}
