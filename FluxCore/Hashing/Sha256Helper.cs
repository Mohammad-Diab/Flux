using System.Security.Cryptography;

namespace FluxCore.Hashing;

/// <summary>SHA-256 hashing utilities for payload integrity verification.</summary>
public static class Sha256Helper
{
    /// <summary>Computes the SHA-256 hash of the data.</summary>
    public static byte[] ComputeHash(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return SHA256.HashData(data);
    }

    /// <summary>Computes the SHA-256 hash of the data span.</summary>
    public static byte[] ComputeHash(ReadOnlySpan<byte> data)
    {
        return SHA256.HashData(data);
    }

    /// <summary>Computes the SHA-256 hash of a stream.</summary>
    public static async Task<byte[]> ComputeHashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    /// <summary>Converts a hash to a lowercase hex string.</summary>
    public static string ToHexString(byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Converts a 64-character hex string to hash bytes.</summary>
    public static byte[] FromHexString(string hexString)
    {
        ArgumentNullException.ThrowIfNull(hexString);

        if (hexString.Length != 64)
            throw new ArgumentException("SHA-256 hex string must be 64 characters.", nameof(hexString));

        return Convert.FromHexString(hexString);
    }

    /// <summary>Verifies data against an expected 32-byte hash.</summary>
    public static bool Verify(byte[] data, byte[] expectedHash)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateExpectedHash(expectedHash);

        return CryptographicOperations.FixedTimeEquals(ComputeHash(data), expectedHash);
    }

    /// <summary>Verifies a stream's content against an expected 32-byte hash.</summary>
    public static bool Verify(Stream stream, byte[] expectedHash)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateExpectedHash(expectedHash);

        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), expectedHash);
    }

    private static void ValidateExpectedHash(byte[] expectedHash)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);

        if (expectedHash.Length != 32)
            throw new ArgumentException("Expected hash must be 32 bytes.", nameof(expectedHash));
    }
}
