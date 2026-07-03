using System.Security.Cryptography;

namespace FluxCore.Hashing;

/// <summary>
/// Provides SHA-256 hashing utilities for payload integrity verification.
/// </summary>
public static class Sha256Helper
{
    /// <summary>
    /// Computes the SHA-256 hash of the provided data.
    /// </summary>
    /// <param name="data">Data to hash.</param>
    /// <returns>32-byte SHA-256 hash.</returns>
    /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
    public static byte[] ComputeHash(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return SHA256.HashData(data);
    }

    /// <summary>
    /// Computes the SHA-256 hash of the provided data span.
    /// </summary>
    /// <param name="data">Data to hash.</param>
    /// <returns>32-byte SHA-256 hash.</returns>
    public static byte[] ComputeHash(ReadOnlySpan<byte> data)
    {
        return SHA256.HashData(data);
    }

    /// <summary>
    /// Computes the SHA-256 hash of a stream asynchronously.
    /// </summary>
    /// <param name="stream">Stream to hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>32-byte SHA-256 hash.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    public static async Task<byte[]> ComputeHashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    /// <summary>
    /// Converts a hash byte array to a hexadecimal string.
    /// </summary>
    /// <param name="hash">Hash bytes.</param>
    /// <returns>Lowercase hexadecimal string representation.</returns>
    public static string ToHexString(byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Converts a hexadecimal string to a hash byte array.
    /// </summary>
    /// <param name="hexString">Hexadecimal string (64 characters for SHA-256).</param>
    /// <returns>32-byte hash array.</returns>
    /// <exception cref="ArgumentException">Thrown when hex string is invalid.</exception>
    public static byte[] FromHexString(string hexString)
    {
        ArgumentNullException.ThrowIfNull(hexString);

        if (hexString.Length != 64)
            throw new ArgumentException("SHA-256 hex string must be 64 characters.", nameof(hexString));

        return Convert.FromHexString(hexString);
    }

    /// <summary>
    /// Verifies that computed hash matches expected hash.
    /// </summary>
    /// <param name="data">Data to verify.</param>
    /// <param name="expectedHash">Expected 32-byte hash.</param>
    /// <returns>True if hashes match; false otherwise.</returns>
    public static bool Verify(byte[] data, byte[] expectedHash)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(expectedHash);

        if (expectedHash.Length != 32)
            throw new ArgumentException("Expected hash must be 32 bytes.", nameof(expectedHash));

        var actualHash = ComputeHash(data);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
