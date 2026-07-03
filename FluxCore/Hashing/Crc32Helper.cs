using System.IO.Hashing;

namespace FluxCore.Hashing;

/// <summary>
/// Provides CRC-32 hashing utilities for per-frame payload verification.
/// Uses the standard CRC-32 algorithm (IEEE 802.3).
/// </summary>
public static class Crc32Helper
{
    /// <summary>
    /// Computes the CRC-32 checksum of the provided data.
    /// </summary>
    /// <param name="data">Data to checksum.</param>
    /// <returns>4-byte CRC-32 checksum (uint32).</returns>
    /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
    public static uint ComputeChecksum(byte[] data)
    {
    ArgumentNullException.ThrowIfNull(data);
    return ComputeChecksum(data.AsSpan());
    }

/// <summary>
    /// Computes the CRC-32 checksum of the provided data span.
    /// </summary>
    /// <param name="data">Data to checksum.</param>
    /// <returns>4-byte CRC-32 checksum (uint32).</returns>
    public static uint ComputeChecksum(ReadOnlySpan<byte> data)
    {
        var hash = Crc32.Hash(data);
  return BitConverter.ToUInt32(hash);
    }

    /// <summary>
    /// Verifies that computed checksum matches expected checksum.
    /// </summary>
    /// <param name="data">Data to verify.</param>
    /// <param name="expectedChecksum">Expected CRC-32 checksum.</param>
    /// <returns>True if checksums match; false otherwise.</returns>
 public static bool Verify(byte[] data, uint expectedChecksum)
    {
        ArgumentNullException.ThrowIfNull(data);
        var actualChecksum = ComputeChecksum(data);
        return actualChecksum == expectedChecksum;
    }

    /// <summary>
    /// Verifies that computed checksum matches expected checksum.
    /// </summary>
  /// <param name="data">Data to verify.</param>
    /// <param name="expectedChecksum">Expected CRC-32 checksum.</param>
    /// <returns>True if checksums match; false otherwise.</returns>
    public static bool Verify(ReadOnlySpan<byte> data, uint expectedChecksum)
    {
   var actualChecksum = ComputeChecksum(data);
        return actualChecksum == expectedChecksum;
    }
}
