using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Sentinel.App.Services;

internal sealed class RecoveryKeyMaterial : IDisposable
{
    private const int SecretSize = 32;
    private const int EncodedSecretCharacters = 52;
    private const int ChecksumCharacters = 6;
    private const string Prefix = "SAI-RK1-";
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static readonly byte[] ChecksumContext = Encoding.ASCII.GetBytes("SentinelAI.RecoveryKey.v1");

    private readonly byte[] _secret;
    private bool _disposed;

    private RecoveryKeyMaterial(byte[] secret)
    {
        if (secret.Length != SecretSize) throw new ArgumentException("Recovery material must contain exactly 256 bits.", nameof(secret));
        _secret = secret;
    }

    internal static RecoveryKeyMaterial Generate() => new(RandomNumberGenerator.GetBytes(SecretSize));

    internal static bool TryParse(string? value, out RecoveryKeyMaterial? material)
    {
        material = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string normalized = value.Trim().ToUpperInvariant();
        if (!normalized.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        string remainder = normalized[Prefix.Length..];
        string[] groups = remainder.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (groups.Length < 2) return false;
        string checksum = groups[^1];
        if (checksum.Length != ChecksumCharacters) return false;

        string encodedSecret = string.Join(string.Empty, groups, 0, groups.Length - 1);
        if (encodedSecret.Length != EncodedSecretCharacters || !TryDecodeBase32(encodedSecret, out byte[] secret))
            return false;

        string expectedChecksum = ComputeChecksum(secret);
        byte[] providedBytes = Encoding.ASCII.GetBytes(checksum);
        byte[] expectedBytes = Encoding.ASCII.GetBytes(expectedChecksum);
        bool valid = CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
        CryptographicOperations.ZeroMemory(providedBytes);
        CryptographicOperations.ZeroMemory(expectedBytes);
        if (!valid)
        {
            CryptographicOperations.ZeroMemory(secret);
            return false;
        }

        material = new RecoveryKeyMaterial(secret);
        return true;
    }

    internal string ToDisplayString()
    {
        ThrowIfDisposed();
        string encoded = EncodeBase32(_secret);
        List<string> groups = new();
        for (int index = 0; index < encoded.Length; index += 4)
            groups.Add(encoded.Substring(index, Math.Min(4, encoded.Length - index)));
        return Prefix + string.Join('-', groups) + "-" + ComputeChecksum(_secret);
    }

    internal RecoveryKeyFileKeyProtector CreateProtector()
    {
        ThrowIfDisposed();
        return new RecoveryKeyFileKeyProtector(_secret);
    }

    internal byte[] ExportForExplicitUserAction()
    {
        ThrowIfDisposed();
        return _secret.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_secret);
        _disposed = true;
    }

    private static string ComputeChecksum(byte[] secret)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(ChecksumContext);
        hash.AppendData(secret);
        byte[] digest = hash.GetHashAndReset();
        try
        {
            return EncodeBase32(digest)[..ChecksumCharacters];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static string EncodeBase32(ReadOnlySpan<byte> data)
    {
        StringBuilder output = new((data.Length * 8 + 4) / 5);
        uint buffer = 0;
        int bits = 0;
        foreach (byte value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output.Append(Base32Alphabet[(int)((buffer >> bits) & 0x1f)]);
            }
        }
        if (bits > 0)
            output.Append(Base32Alphabet[(int)((buffer << (5 - bits)) & 0x1f)]);
        return output.ToString();
    }

    private static bool TryDecodeBase32(string encoded, out byte[] data)
    {
        data = Array.Empty<byte>();
        byte[] output = new byte[SecretSize];
        uint buffer = 0;
        int bits = 0;
        int written = 0;
        foreach (char character in encoded)
        {
            int value = Base32Alphabet.IndexOf(character);
            if (value < 0)
            {
                CryptographicOperations.ZeroMemory(output);
                return false;
            }

            buffer = (buffer << 5) | (uint)value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                if (written >= output.Length)
                {
                    CryptographicOperations.ZeroMemory(output);
                    return false;
                }
                output[written++] = (byte)(buffer >> bits);
                buffer &= bits == 0 ? 0u : (1u << bits) - 1;
            }
        }

        if (written != SecretSize || (bits > 0 && buffer != 0))
        {
            CryptographicOperations.ZeroMemory(output);
            return false;
        }

        data = output;
        return true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
