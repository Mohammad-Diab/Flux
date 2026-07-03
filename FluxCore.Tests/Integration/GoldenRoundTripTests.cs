using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;
using FluxCore.Imaging;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Integration;

/// <summary>
/// Milestone A quality gate: a payload encoded to PNG frames and decoded back must be
/// byte-identical, verified end to end by SHA-256. This suite freezes the codec contract.
/// </summary>
public class GoldenRoundTripTests
{
    internal static byte[] DeterministicPayload(int length, int seed = 2024)
    {
        var random = new Random(seed);
        var payload = new byte[length];
        random.NextBytes(payload);
        return payload;
    }

    internal static List<byte[]> EncodeTransfer(byte[] payload, EccLevel level, string originalName)
    {
        int bytesPerFrame = level.PayloadBytesPerFrame();
        uint payloadFrames = (uint)((payload.Length + bytesPerFrame - 1) / bytesPerFrame);
        if (payloadFrames == 0)
            payloadFrames = 1;
        uint totalFrames = payloadFrames + 1;

        var metadata = new MetadataPayload(
            sha256: Sha256Helper.ComputeHash(payload),
            payloadType: PayloadType.Raw,
            eccLevel: level,
            totalFrames: totalFrames,
            payloadLength: payload.Length,
            originalName: originalName,
            originalLength: payload.Length,
            contentSignature: DeterministicPayload(32, seed: 1),
            colorMap: ColorMap.Default);

        var pngs = new List<byte[]>
        {
            FrameRenderer.RenderPng(
                FrameEncoder.BuildMetadataFrame(metadata.Serialize(), totalFrames),
                ColorMap.Default),
        };

        for (uint frameId = 1; frameId <= payloadFrames; frameId++)
        {
            int offset = (int)(frameId - 1) * bytesPerFrame;
            int length = Math.Min(bytesPerFrame, payload.Length - offset);
            var slice = payload.AsSpan(offset, Math.Max(0, length));

            pngs.Add(FrameRenderer.RenderPng(
                FrameEncoder.BuildFrame(frameId, totalFrames, slice, level),
                ColorMap.Default));
        }

        return pngs;
    }

    internal static (MetadataPayload Metadata, byte[] Payload) DecodeTransfer(
        IReadOnlyList<byte[]> pngs, Func<byte[], SKBitmap>? loader = null)
    {
        loader ??= SKBitmap.Decode;
        var decoder = new FrameDecoder(ColorMap.Default);

        using var frame0 = loader(pngs[0]);
        var metadataResult = decoder.DecodeMetadataFrame(frame0);
        Assert.Equal(DecodeStatus.Success, metadataResult.Status);
        Assert.True(metadataResult.Header!.Value.IsMetadataFrame);

        var metadata = MetadataPayload.Deserialize(metadataResult.Payload!);
        Assert.True(metadata.MatchesFrameFormat());
        Assert.Equal((uint)pngs.Count, metadata.TotalFrames);

        var payload = new byte[metadata.PayloadLength];
        int written = 0;

        for (int i = 1; i < pngs.Count; i++)
        {
            using var bitmap = loader(pngs[i]);
            var result = decoder.Decode(bitmap, expectedFrameId: (uint)i);
            Assert.Equal(DecodeStatus.Success, result.Status);

            result.Payload!.CopyTo(payload.AsSpan(written));
            written += result.Payload.Length;
        }

        Assert.Equal(metadata.PayloadLength, written);
        Assert.True(Sha256Helper.Verify(payload, metadata.Sha256),
            "Reassembled payload SHA-256 does not match the declared hash.");

        return (metadata, payload);
    }

    [Fact]
    public void RoundTrip_MultiFramePayload_Sha256Verified()
    {
        var payload = DeterministicPayload(25_000);
        var pngs = EncodeTransfer(payload, EccLevel.Medium, "golden.bin");

        Assert.Equal(4, pngs.Count);
        var (metadata, decoded) = DecodeTransfer(pngs);

        Assert.Equal(payload, decoded);
        Assert.Equal("golden.bin", metadata.OriginalName);
    }

    [Fact]
    public void RoundTrip_SingleFramePayload()
    {
        var payload = DeterministicPayload(4_000);
        var pngs = EncodeTransfer(payload, EccLevel.Medium, "small.bin");

        Assert.Equal(2, pngs.Count);
        var (_, decoded) = DecodeTransfer(pngs);

        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void RoundTrip_ExactMultipleOfFrameCapacity_NoPaddingAmbiguity()
    {
        var payload = DeterministicPayload(2 * EccLevel.Medium.PayloadBytesPerFrame());
        var pngs = EncodeTransfer(payload, EccLevel.Medium, "exact.bin");

        Assert.Equal(3, pngs.Count);
        var (_, decoded) = DecodeTransfer(pngs);

        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void RoundTrip_TinyPayload()
    {
        var payload = "hello flux"u8.ToArray();
        var pngs = EncodeTransfer(payload, EccLevel.Medium, "tiny.txt");

        var (_, decoded) = DecodeTransfer(pngs);

        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void RoundTrip_LongUnicodeName_SurvivesFrameZero()
    {
        var payload = DeterministicPayload(1_000);
        var name = "Отчёт-第四季度-📊-" + new string('x', 200) + ".7z";
        var pngs = EncodeTransfer(payload, EccLevel.Medium, name);

        var (metadata, decoded) = DecodeTransfer(pngs);

        Assert.Equal(name, metadata.OriginalName);
        Assert.Equal(payload, decoded);
    }

    [Theory]
    [InlineData(EccLevel.Low)]
    [InlineData(EccLevel.High)]
    [InlineData(EccLevel.Max)]
    public void RoundTrip_AllEccLevels(EccLevel level)
    {
        var payload = DeterministicPayload(level.PayloadBytesPerFrame() + 500);
        var pngs = EncodeTransfer(payload, level, "levels.bin");

        var (_, decoded) = DecodeTransfer(pngs);

        Assert.Equal(payload, decoded);
    }
}
