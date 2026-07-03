using FluxCore.Ecc;

using FluxCore.Hashing;

namespace FluxCore.Framing;

/// <summary>
/// Builds the complete tile map for one frame: computes the header (payload length, CRC-32),
/// protects it as three redundant RS(48,16) copies, encodes the payload into 53 interleaved
/// RS(255,k) codewords, and assigns every tile its value.
/// </summary>
public static class FrameEncoder
{
    /// <summary>
    /// Builds the tile map for one frame.
    /// </summary>
    /// <param name="frameId">Frame ID (0-based; frame 0 is the metadata frame).</param>
    /// <param name="totalFrames">Total frames in the transfer, including frame 0.</param>
    /// <param name="payload">This frame's payload; at most the level's per-frame capacity.</param>
    /// <param name="eccLevel">ECC level for the payload codewords.</param>
    /// <param name="isMetadataFrame">Whether this is the metadata frame (must imply frame id 0).</param>
    public static FrameTileMap BuildFrame(
        uint frameId,
        uint totalFrames,
        ReadOnlySpan<byte> payload,
        EccLevel eccLevel,
        bool isMetadataFrame = false)
    {
        if (payload.Length > eccLevel.PayloadBytesPerFrame())
            throw new ArgumentException(
                $"Payload of {payload.Length} bytes exceeds the {eccLevel.PayloadBytesPerFrame()}-byte frame capacity at level {eccLevel}.",
                nameof(payload));
        if (isMetadataFrame && frameId != 0)
            throw new ArgumentException("The metadata frame must be frame 0.", nameof(frameId));

        var header = new FrameHeader(
            frameId,
            totalFrames,
            (ushort)payload.Length,
            Crc32Helper.ComputeChecksum(payload),
            eccLevel,
            isMetadataFrame);

        var tiles = new byte[FrameFormat.TotalTiles];

        WriteHeaderCopies(header, tiles);
        WritePayloadCodewords(payload, eccLevel, tiles);

        return new FrameTileMap(header, tiles);
    }

    private static void WriteHeaderCopies(in FrameHeader header, byte[] tiles)
    {
        Span<byte> symbols = stackalloc byte[ReedSolomonBlockCodec.EncodedHeaderLength];
        ReedSolomonBlockCodec.EncodeHeader(header, symbols);

        for (int copy = 0; copy < FrameFormat.HeaderCopyCount; copy++)
        {
            var positions = FrameFormat.GetHeaderCopyTiles(copy);
            for (int i = 0; i < FrameFormat.HeaderCopyLength; i++)
            {
                var (x, y) = positions[i];
                tiles[y * FrameFormat.GridWidthTiles + x] = symbols[i];
            }
        }
    }

    private static void WritePayloadCodewords(ReadOnlySpan<byte> payload, EccLevel eccLevel, byte[] tiles)
    {
        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        ReedSolomonBlockCodec.EncodePayload(payload, eccLevel, codewords);

        for (int t = 0; t < FrameFormat.DataTileCount; t++)
        {
            var (codeword, symbol) = FrameFormat.ToCodewordSymbol(t);
            var (x, y) = FrameFormat.DataTiles[t];
            tiles[y * FrameFormat.GridWidthTiles + x] = codewords[codeword * FrameFormat.CodewordLength + symbol];
        }
    }
}
