using FluxCore.Framing;
using ZXing.Common.ReedSolomon;

namespace FluxCore.Ecc;

/// <summary>
/// Reed-Solomon codec over GF(256) operating in proper 255-symbol blocks: a frame payload is
/// split across 53 independent RS(255,k) codewords, and the 16-byte frame header is protected
/// as a single RS(48,16) codeword. Decoding corrects symbols in place and reports failure via
/// return value (ZXing's decoder does not throw on unrecoverable input).
/// </summary>
public static class ReedSolomonBlockCodec
{
    /// <summary>Total symbols produced for one frame payload: 53 codewords x 255 symbols.</summary>
    public const int EncodedFrameLength = FrameFormat.CodewordCount * FrameFormat.CodewordLength;

    /// <summary>Data symbols in the header codeword (the serialized <see cref="FrameHeader"/>).</summary>
    public const int HeaderDataLength = FrameHeader.Size;

    /// <summary>Total symbols in the encoded header codeword: RS(48,16).</summary>
    public const int EncodedHeaderLength = FrameFormat.HeaderCopyLength;

    private const int HeaderParitySymbols = EncodedHeaderLength - HeaderDataLength;

    private static readonly ThreadLocal<ReedSolomonEncoder> Encoder =
        new(() => new ReedSolomonEncoder(GenericGF.QR_CODE_FIELD_256));

    private static readonly ThreadLocal<ReedSolomonDecoder> Decoder =
        new(() => new ReedSolomonDecoder(GenericGF.QR_CODE_FIELD_256));

    /// <summary>
    /// Encodes a frame payload into 53 RS(255,k) codewords, laid out codeword-major:
    /// codeword c occupies destination[c*255 .. (c+1)*255). Payload bytes are distributed
    /// contiguously: codeword c carries payload[c*k .. (c+1)*k), zero-padded past the end.
    /// </summary>
    /// <param name="payload">Frame payload, at most 53 x k bytes for the level.</param>
    /// <param name="level">ECC level determining k.</param>
    /// <param name="destination">Destination for the encoded symbols, at least <see cref="EncodedFrameLength"/> bytes.</param>
    public static void EncodePayload(ReadOnlySpan<byte> payload, EccLevel level, Span<byte> destination)
    {
        int k = level.DataBytesPerCodeword();

        if (payload.Length > FrameFormat.CodewordCount * k)
            throw new ArgumentException(
                $"Payload of {payload.Length} bytes exceeds frame capacity {FrameFormat.CodewordCount * k} at level {level}.",
                nameof(payload));
        if (destination.Length < EncodedFrameLength)
            throw new ArgumentException(
                $"Destination must be at least {EncodedFrameLength} bytes.", nameof(destination));

        int parity = level.ParitySymbols();
        var codeword = new int[FrameFormat.CodewordLength];

        for (int c = 0; c < FrameFormat.CodewordCount; c++)
        {
            Array.Clear(codeword);

            int payloadStart = c * k;
            int available = Math.Max(0, Math.Min(k, payload.Length - payloadStart));
            for (int i = 0; i < available; i++)
            {
                codeword[i] = payload[payloadStart + i];
            }

            Encoder.Value!.encode(codeword, parity);

            var target = destination.Slice(c * FrameFormat.CodewordLength, FrameFormat.CodewordLength);
            for (int i = 0; i < FrameFormat.CodewordLength; i++)
            {
                target[i] = (byte)codeword[i];
            }
        }
    }

    /// <summary>
    /// Decodes 53 RS(255,k) codewords (codeword-major layout), correcting errors in place,
    /// and writes the concatenated 53 x k data bytes to <paramref name="payload"/>.
    /// </summary>
    /// <param name="codewords">Encoded symbols, at least <see cref="EncodedFrameLength"/> bytes; corrected in place.</param>
    /// <param name="level">ECC level determining k.</param>
    /// <param name="payload">Destination for decoded data bytes, at least 53 x k bytes.</param>
    /// <param name="correctedErrors">Total symbols corrected across all codewords.</param>
    /// <returns>True if every codeword decoded; false if any codeword is damaged beyond repair.</returns>
    public static bool TryDecodePayload(Span<byte> codewords, EccLevel level, Span<byte> payload, out int correctedErrors)
    {
        int k = level.DataBytesPerCodeword();

        if (codewords.Length < EncodedFrameLength)
            throw new ArgumentException(
                $"Codewords must be at least {EncodedFrameLength} bytes.", nameof(codewords));
        if (payload.Length < FrameFormat.CodewordCount * k)
            throw new ArgumentException(
                $"Payload destination must be at least {FrameFormat.CodewordCount * k} bytes.", nameof(payload));

        correctedErrors = 0;
        int parity = level.ParitySymbols();
        var work = new int[FrameFormat.CodewordLength];

        for (int c = 0; c < FrameFormat.CodewordCount; c++)
        {
            var source = codewords.Slice(c * FrameFormat.CodewordLength, FrameFormat.CodewordLength);
            for (int i = 0; i < FrameFormat.CodewordLength; i++)
            {
                work[i] = source[i];
            }

            if (!Decoder.Value!.decode(work, parity))
                return false;

            for (int i = 0; i < FrameFormat.CodewordLength; i++)
            {
                if (source[i] != (byte)work[i])
                {
                    correctedErrors++;
                    source[i] = (byte)work[i];
                }
            }

            for (int i = 0; i < k; i++)
            {
                payload[c * k + i] = (byte)work[i];
            }
        }

        return true;
    }

    /// <summary>Encodes a frame header as one RS(48,16) codeword.</summary>
    /// <param name="header">Header to encode.</param>
    /// <param name="destination">Destination for the 48 encoded symbols.</param>
    public static void EncodeHeader(in FrameHeader header, Span<byte> destination)
    {
        if (destination.Length < EncodedHeaderLength)
            throw new ArgumentException(
                $"Destination must be at least {EncodedHeaderLength} bytes.", nameof(destination));

        Span<byte> serialized = stackalloc byte[FrameHeader.Size];
        header.Serialize(serialized);

        var codeword = new int[EncodedHeaderLength];
        for (int i = 0; i < FrameHeader.Size; i++)
        {
            codeword[i] = serialized[i];
        }

        Encoder.Value!.encode(codeword, HeaderParitySymbols);

        for (int i = 0; i < EncodedHeaderLength; i++)
        {
            destination[i] = (byte)codeword[i];
        }
    }

    /// <summary>
    /// Decodes one RS(48,16) header codeword. Corrects up to 16 symbol errors. The returned
    /// header is not validated; callers vote across copies and check <see cref="FrameHeader.IsPlausible"/>.
    /// </summary>
    /// <param name="symbols">The 48 encoded header symbols.</param>
    /// <param name="header">Decoded header when successful.</param>
    /// <returns>True if the codeword decoded; false if damaged beyond repair.</returns>
    public static bool TryDecodeHeader(ReadOnlySpan<byte> symbols, out FrameHeader header)
    {
        if (symbols.Length < EncodedHeaderLength)
            throw new ArgumentException(
                $"Symbols must be at least {EncodedHeaderLength} bytes.", nameof(symbols));

        var work = new int[EncodedHeaderLength];
        for (int i = 0; i < EncodedHeaderLength; i++)
        {
            work[i] = symbols[i];
        }

        if (!Decoder.Value!.decode(work, HeaderParitySymbols))
        {
            header = default;
            return false;
        }

        Span<byte> serialized = stackalloc byte[FrameHeader.Size];
        for (int i = 0; i < FrameHeader.Size; i++)
        {
            serialized[i] = (byte)work[i];
        }

        header = FrameHeader.Deserialize(serialized);
        return true;
    }
}
