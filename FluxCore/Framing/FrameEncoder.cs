using FluxCore.Ecc;
using FluxCore.Hashing;
using FluxCore.Imaging;

namespace FluxCore.Framing;

/// <summary>
/// Builds the complete tile map for one frame. Payload frames (1..N) carry a 16-byte header as
/// three redundant RS(48,16) copies plus 53 interleaved RS(255,k) palette codewords. The
/// metadata frame (frame 0) is a self-contained 8-color frame: its header and data tiles all
/// carry cube-corner colors (3 bits each) protected by 12 interleaved RS(255,127) codewords,
/// so it is decodable by pure threshold and independent of the payload palette.
/// </summary>
public static class FrameEncoder
{
    /// <summary>
    /// Builds the tile map for a payload frame (frame id >= 1).
    /// </summary>
    /// <param name="frameId">Frame ID (>= 1).</param>
    /// <param name="totalFrames">Total frames in the transfer, including frame 0.</param>
    /// <param name="payload">This frame's payload; at most the level's per-frame capacity at this depth.</param>
    /// <param name="eccLevel">ECC level for the payload codewords.</param>
    /// <param name="bitsPerTile">Colour depth (bits per tile); 8 is one palette byte per tile.</param>
    public static FrameTileMap BuildFrame(
        uint frameId,
        uint totalFrames,
        ReadOnlySpan<byte> payload,
        EccLevel eccLevel,
        int bitsPerTile = 8)
    {
        int codewordCount = FrameFormat.CodewordsForBits(bitsPerTile);
        int capacity = eccLevel.PayloadBytesPerFrame(codewordCount);
        if (payload.Length > capacity)
            throw new ArgumentException(
                $"Payload of {payload.Length} bytes exceeds the {capacity}-byte frame capacity at level {eccLevel}, {bitsPerTile} bits/tile.",
                nameof(payload));

        var header = new FrameHeader(
            frameId,
            totalFrames,
            (ushort)payload.Length,
            Crc32Helper.ComputeChecksum(payload),
            eccLevel);

        var tiles = new byte[FrameFormat.TotalTiles];

        WriteHeaderCopies(header, tiles);
        WritePayloadCodewords(payload, eccLevel, tiles, bitsPerTile);

        return new FrameTileMap(header, tiles);
    }

    /// <summary>
    /// Builds the metadata frame (frame 0) as a self-contained 8-color frame.
    /// </summary>
    /// <param name="content">Serialized metadata; at most <see cref="FrameFormat.MetadataContentBytes"/> bytes.</param>
    /// <param name="totalFrames">Total frames in the transfer, including frame 0.</param>
    public static FrameTileMap BuildMetadataFrame(ReadOnlySpan<byte> content, uint totalFrames)
    {
        if (content.Length > FrameFormat.MetadataContentBytes)
            throw new ArgumentException(
                $"Metadata content of {content.Length} bytes exceeds the {FrameFormat.MetadataContentBytes}-byte capacity.",
                nameof(content));

        var padded = new byte[FrameFormat.MetadataContentBytes];
        content.CopyTo(padded);

        var stream = new byte[FrameFormat.MetadataEncodedBytes];
        Span<byte> block = stackalloc byte[FrameFormat.CodewordLength];
        for (int c = 0; c < FrameFormat.MetadataCodewordCount; c++)
        {
            ReedSolomonBlockCodec.EncodeBlock(
                padded.AsSpan(c * FrameFormat.MetadataCodewordDataBytes, FrameFormat.MetadataCodewordDataBytes),
                FrameFormat.MetadataParitySymbols,
                block);

            for (int s = 0; s < FrameFormat.CodewordLength; s++)
            {
                stream[s * FrameFormat.MetadataCodewordCount + c] = block[s];
            }
        }

        var tiles = new byte[FrameFormat.TotalTiles];
        var positions = FrameFormat.MetadataFrameTiles;
        var packed = TileBitPacker.Pack(stream, CubeCornerColors.BitsPerTile);
        for (int t = 0; t < FrameFormat.MetadataTilesUsed; t++)
        {
            var (x, y) = positions[t];
            tiles[y * FrameFormat.GridWidthTiles + x] = packed[t];
        }

        var header = new FrameHeader(0, totalFrames, (ushort)content.Length, 0, EccLevel.Max, isMetadataFrame: true);
        return new FrameTileMap(header, tiles, TileColorScheme.CubeCorner8);
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

    private static void WritePayloadCodewords(ReadOnlySpan<byte> payload, EccLevel eccLevel, byte[] tiles, int bitsPerTile)
    {
        int codewordCount = FrameFormat.CodewordsForBits(bitsPerTile);
        int encodedLength = codewordCount * FrameFormat.CodewordLength;

        var codewords = new byte[encodedLength];
        ReedSolomonBlockCodec.EncodePayload(payload, eccLevel, codewords, codewordCount);

        // Stride-interleave so a contiguous tile smear spreads across all codewords, then pack at the depth.
        var stream = new byte[encodedLength];
        for (int c = 0; c < codewordCount; c++)
            for (int s = 0; s < FrameFormat.CodewordLength; s++)
                stream[s * codewordCount + c] = codewords[c * FrameFormat.CodewordLength + s];

        var packed = TileBitPacker.Pack(stream, bitsPerTile);
        for (int t = 0; t < packed.Length; t++)
        {
            var (x, y) = FrameFormat.DataTiles[t];
            tiles[y * FrameFormat.GridWidthTiles + x] = packed[t];
        }
    }
}
