using System.IO.Hashing;

namespace FluxCore.Hashing;

/// <summary>CRC-32 (IEEE 802.3) utilities for per-frame payload verification.</summary>
public static class Crc32Helper
{
    /// <summary>Computes the CRC-32 checksum of the data.</summary>
    public static uint ComputeChecksum(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ComputeChecksum(data.AsSpan());
    }

    /// <summary>Computes the CRC-32 checksum of the data span.</summary>
    public static uint ComputeChecksum(ReadOnlySpan<byte> data)
    {
        var hash = Crc32.Hash(data);
        return BitConverter.ToUInt32(hash);
    }

    /// <summary>Verifies data against an expected checksum.</summary>
    public static bool Verify(byte[] data, uint expectedChecksum)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ComputeChecksum(data) == expectedChecksum;
    }

    /// <summary>Verifies a data span against an expected checksum.</summary>
    public static bool Verify(ReadOnlySpan<byte> data, uint expectedChecksum)
    {
        return ComputeChecksum(data) == expectedChecksum;
    }
}
