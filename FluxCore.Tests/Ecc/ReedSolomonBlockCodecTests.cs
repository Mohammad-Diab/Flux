using FluxCore.Ecc;
using FluxCore.Framing;
using Xunit;

namespace FluxCore.Tests.Ecc;

public class ReedSolomonBlockCodecTests
{
    private static byte[] DeterministicPayload(int length, int seed = 1234)
    {
        var random = new Random(seed);
        var payload = new byte[length];
        random.NextBytes(payload);
        return payload;
    }

    [Theory]
    [InlineData(EccLevel.Low)]
    [InlineData(EccLevel.Medium)]
    [InlineData(EccLevel.High)]
    [InlineData(EccLevel.Max)]
    public void EncodeDecode_RoundTrips_FullCapacityPayload(EccLevel level)
    {
        var payload = DeterministicPayload(level.PayloadBytesPerFrame());
        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        var decoded = new byte[level.PayloadBytesPerFrame()];

        ReedSolomonBlockCodec.EncodePayload(payload, level, codewords);
        var success = ReedSolomonBlockCodec.TryDecodePayload(codewords, level, decoded, out var corrected);

        Assert.True(success);
        Assert.Equal(0, corrected);
        Assert.Equal(payload, decoded);
    }

    [Theory]
    [InlineData(EccLevel.Medium, 1)]
    [InlineData(EccLevel.Medium, 100)]
    [InlineData(EccLevel.Max, 862)]
    public void EncodeDecode_RoundTrips_PartialPayload_ZeroPadded(EccLevel level, int payloadLength)
    {
        var payload = DeterministicPayload(payloadLength);
        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        var decoded = new byte[level.PayloadBytesPerFrame()];

        ReedSolomonBlockCodec.EncodePayload(payload, level, codewords);
        var success = ReedSolomonBlockCodec.TryDecodePayload(codewords, level, decoded, out _);

        Assert.True(success);
        Assert.Equal(payload, decoded[..payloadLength]);
        Assert.All(decoded[payloadLength..], b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(EccLevel.Low)]
    [InlineData(EccLevel.Medium)]
    [InlineData(EccLevel.High)]
    [InlineData(EccLevel.Max)]
    public void TryDecodePayload_CorrectsExactlyMaxErrorsPerCodeword(EccLevel level)
    {
        int t = level.CorrectableErrorsPerCodeword();
        var payload = DeterministicPayload(level.PayloadBytesPerFrame());
        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        ReedSolomonBlockCodec.EncodePayload(payload, level, codewords);

        for (int i = 0; i < t; i++)
        {
            codewords[i * 2] ^= 0xA5;
        }

        var decoded = new byte[level.PayloadBytesPerFrame()];
        var success = ReedSolomonBlockCodec.TryDecodePayload(codewords, level, decoded, out var corrected);

        Assert.True(success);
        Assert.Equal(t, corrected);
        Assert.Equal(payload, decoded);
    }

    [Theory]
    [InlineData(EccLevel.Low)]
    [InlineData(EccLevel.Medium)]
    public void TryDecodePayload_FailsBeyondCorrectionCapacity(EccLevel level)
    {
        int t = level.CorrectableErrorsPerCodeword();
        var payload = DeterministicPayload(level.PayloadBytesPerFrame());
        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        ReedSolomonBlockCodec.EncodePayload(payload, level, codewords);

        for (int i = 0; i <= t; i++)
        {
            codewords[i] ^= 0xFF;
        }

        var decoded = new byte[level.PayloadBytesPerFrame()];
        var success = ReedSolomonBlockCodec.TryDecodePayload(codewords, level, decoded, out _);

        Assert.False(success);
    }

    [Fact]
    public void TryDecodePayload_CorrectsDamageInPlace()
    {
        var payload = DeterministicPayload(EccLevel.Medium.PayloadBytesPerFrame());
        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        ReedSolomonBlockCodec.EncodePayload(payload, EccLevel.Medium, codewords);
        var pristine = codewords.ToArray();

        codewords[10] ^= 0x55;
        codewords[300] ^= 0x55;

        var decoded = new byte[EccLevel.Medium.PayloadBytesPerFrame()];
        var success = ReedSolomonBlockCodec.TryDecodePayload(codewords, EccLevel.Medium, decoded, out var corrected);

        Assert.True(success);
        Assert.Equal(2, corrected);
        Assert.Equal(pristine, codewords);
    }

    [Fact]
    public void ContiguousBurstOf400Tiles_RecoversAtMedium_ThroughInterleaver()
    {
        var payload = DeterministicPayload(EccLevel.Medium.PayloadBytesPerFrame());
        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        ReedSolomonBlockCodec.EncodePayload(payload, EccLevel.Medium, codewords);

        var tileSymbols = new byte[FrameFormat.DataTileCount];
        for (int t = 0; t < FrameFormat.DataTileCount; t++)
        {
            var (c, s) = FrameFormat.ToCodewordSymbol(t);
            tileSymbols[t] = codewords[c * FrameFormat.CodewordLength + s];
        }

        for (int t = 5000; t < 5400; t++)
        {
            tileSymbols[t] ^= 0xFF;
        }

        var damaged = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        for (int t = 0; t < FrameFormat.DataTileCount; t++)
        {
            var (c, s) = FrameFormat.ToCodewordSymbol(t);
            damaged[c * FrameFormat.CodewordLength + s] = tileSymbols[t];
        }

        var decoded = new byte[EccLevel.Medium.PayloadBytesPerFrame()];
        var success = ReedSolomonBlockCodec.TryDecodePayload(damaged, EccLevel.Medium, decoded, out var corrected);

        Assert.True(success);
        Assert.Equal(400, corrected);
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void EncodePayload_ThrowsWhenPayloadExceedsCapacity()
    {
        var payload = new byte[EccLevel.Medium.PayloadBytesPerFrame() + 1];
        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];

        Assert.Throws<ArgumentException>(() =>
            ReedSolomonBlockCodec.EncodePayload(payload, EccLevel.Medium, codewords));
    }

    [Fact]
    public void EncodeHeader_TryDecodeHeader_RoundTrips()
    {
        var header = new FrameHeader(42, 317, 10123, 0xCAFEBABE, EccLevel.Medium);
        var symbols = new byte[ReedSolomonBlockCodec.EncodedHeaderLength];

        ReedSolomonBlockCodec.EncodeHeader(header, symbols);
        var success = ReedSolomonBlockCodec.TryDecodeHeader(symbols, out var decoded);

        Assert.True(success);
        Assert.Equal(header, decoded);
        Assert.True(decoded.IsPlausible());
    }

    [Fact]
    public void TryDecodeHeader_Survives16Of48CorruptSymbols()
    {
        var header = new FrameHeader(7, 100, 5000, 0x1234, EccLevel.High);
        var symbols = new byte[ReedSolomonBlockCodec.EncodedHeaderLength];
        ReedSolomonBlockCodec.EncodeHeader(header, symbols);

        for (int i = 0; i < 16; i++)
        {
            symbols[i * 3] ^= 0xFF;
        }

        var success = ReedSolomonBlockCodec.TryDecodeHeader(symbols, out var decoded);

        Assert.True(success);
        Assert.Equal(header, decoded);
    }

    [Fact]
    public void TryDecodeHeader_FailsBeyond16CorruptSymbols()
    {
        var header = new FrameHeader(7, 100, 5000, 0x1234, EccLevel.High);
        var symbols = new byte[ReedSolomonBlockCodec.EncodedHeaderLength];
        ReedSolomonBlockCodec.EncodeHeader(header, symbols);

        for (int i = 0; i < 20; i++)
        {
            symbols[i] ^= 0xFF;
        }

        var success = ReedSolomonBlockCodec.TryDecodeHeader(symbols, out var decoded);

        Assert.False(success || decoded.IsPlausible() && decoded == header,
            "Beyond-capacity corruption must not silently return the original header.");
    }

    [Fact]
    public void EncodePayload_IsThreadSafe_AcrossParallelFrames()
    {
        var results = new byte[8][];

        Parallel.For(0, 8, i =>
        {
            var payload = DeterministicPayload(EccLevel.Medium.PayloadBytesPerFrame(), seed: 42);
            var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
            ReedSolomonBlockCodec.EncodePayload(payload, EccLevel.Medium, codewords);
            results[i] = codewords;
        });

        for (int i = 1; i < results.Length; i++)
        {
            Assert.Equal(results[0], results[i]);
        }
    }
}
