using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Decoding;

public class FrameDecoderTests
{
    private static readonly FrameDecoder Decoder = new(ColorMap.Default);

    private static byte[] DeterministicPayload(int length, int seed = 11)
    {
        var random = new Random(seed);
        var payload = new byte[length];
        random.NextBytes(payload);
        return payload;
    }

    private static SKBitmap RenderFrame(uint frameId, uint totalFrames, byte[] payload, EccLevel level,
        bool isMetadata = false)
    {
        var map = FrameEncoder.BuildFrame(frameId, totalFrames, payload, level, isMetadata);
        return SKBitmap.Decode(FrameRenderer.RenderPng(map, ColorMap.Default));
    }

    [Theory]
    [InlineData(EccLevel.Low)]
    [InlineData(EccLevel.Medium)]
    [InlineData(EccLevel.Max)]
    public void Decode_PristineFrame_RecoversPayloadExactly(EccLevel level)
    {
        var payload = DeterministicPayload(level.PayloadBytesPerFrame());
        using var bitmap = RenderFrame(3, 10, payload, level);

        var result = Decoder.Decode(bitmap);

        Assert.Equal(DecodeStatus.Success, result.Status);
        Assert.Equal(3u, result.Header!.Value.FrameId);
        Assert.Equal(payload, result.Payload);
        Assert.Equal(0, result.Diagnostics.CorrectedErrors);
        Assert.Equal(3, result.Diagnostics.HeaderCopiesAgreeing);
    }

    [Fact]
    public void Decode_PartialPayloadFrame_ReturnsExactlyPayloadLengthBytes()
    {
        var payload = DeterministicPayload(1234);
        using var bitmap = RenderFrame(9, 10, payload, EccLevel.Medium);

        var result = Decoder.Decode(bitmap);

        Assert.Equal(DecodeStatus.Success, result.Status);
        Assert.Equal(1234, result.Payload!.Length);
        Assert.Equal(payload, result.Payload);
    }

    [Fact]
    public void Decode_MetadataFrame_RoundTripsMetadata()
    {
        var metadata = new MetadataPayload(
            sha256: DeterministicPayload(32),
            payloadType: PayloadType.SevenZip,
            eccLevel: EccLevel.Medium,
            totalFrames: 7,
            payloadLength: 60_000,
            originalName: "archive.7z",
            originalLength: 150_000,
            contentSignature: DeterministicPayload(32, seed: 2),
            colorMap: ColorMap.Default);
        using var bitmap = RenderFrame(0, 7, metadata.Serialize(), EccLevel.Max, isMetadata: true);

        var result = Decoder.Decode(bitmap);

        Assert.Equal(DecodeStatus.Success, result.Status);
        Assert.True(result.Header!.Value.IsMetadataFrame);

        var restored = MetadataPayload.Deserialize(result.Payload!);
        Assert.Equal("archive.7z", restored.OriginalName);
        Assert.True(restored.MatchesFrameFormat());
    }

    [Fact]
    public void Decode_SameFrameAsBefore_ShortCircuits()
    {
        using var bitmap = RenderFrame(4, 10, DeterministicPayload(500), EccLevel.Medium);

        var result = Decoder.Decode(bitmap, previousFrameId: 4);

        Assert.Equal(DecodeStatus.SameFrameAsBefore, result.Status);
        Assert.Equal(4u, result.Header!.Value.FrameId);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void Decode_WrongFrame_ReportsDecodedHeader()
    {
        using var bitmap = RenderFrame(6, 10, DeterministicPayload(500), EccLevel.Medium);

        var result = Decoder.Decode(bitmap, previousFrameId: 4, expectedFrameId: 5);

        Assert.Equal(DecodeStatus.WrongFrame, result.Status);
        Assert.Equal(6u, result.Header!.Value.FrameId);
    }

    [Fact]
    public void Decode_ScaledCapture_StillSucceeds()
    {
        var payload = DeterministicPayload(EccLevel.Medium.PayloadBytesPerFrame());
        using var source = RenderFrame(2, 5, payload, EccLevel.Medium);
        var info = new SKImageInfo((int)(source.Width * 1.25), (int)(source.Height * 1.25), SKColorType.Rgba8888);
        using var scaled = source.Resize(info, SKFilterQuality.High)!;

        var result = Decoder.Decode(scaled);

        Assert.Equal(DecodeStatus.Success, result.Status);
        Assert.Equal(payload, result.Payload);
    }

    [Fact]
    public void Decode_OffsetCaptureOnGrayBackground_StillSucceeds()
    {
        var payload = DeterministicPayload(4000);
        using var source = RenderFrame(1, 3, payload, EccLevel.Medium);
        using var padded = new SKBitmap(source.Width + 140, source.Height + 90, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(padded))
        {
            canvas.Clear(new SKColor(210, 210, 210));
            canvas.DrawBitmap(source, 70, 40);
        }

        var result = Decoder.Decode(padded);

        Assert.Equal(DecodeStatus.Success, result.Status);
        Assert.Equal(payload, result.Payload);
    }

    [Fact]
    public void Decode_Rotated180Capture_ResolvedByTimingOrientation()
    {
        var payload = DeterministicPayload(4000);
        using var source = RenderFrame(1, 3, payload, EccLevel.Medium);
        using var rotated = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.Clear(SKColors.White);
            canvas.RotateDegrees(180, source.Width / 2f, source.Height / 2f);
            canvas.DrawBitmap(source, 0, 0);
        }

        var result = Decoder.Decode(rotated);

        Assert.Equal(DecodeStatus.Success, result.Status);
        Assert.Equal(payload, result.Payload);
    }

    [Fact]
    public void Decode_DamagedTiles_CorrectedByEcc()
    {
        var payload = DeterministicPayload(EccLevel.Medium.PayloadBytesPerFrame());
        using var bitmap = RenderFrame(2, 5, payload, EccLevel.Medium);

        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = new SKColor(128, 128, 128), IsAntialias = false })
        {
            canvas.DrawRect(400, 300, 90, 90, paint);
        }

        var result = Decoder.Decode(bitmap);

        Assert.Equal(DecodeStatus.Success, result.Status);
        Assert.Equal(payload, result.Payload);
        Assert.True(result.Diagnostics.CorrectedErrors > 0);
    }

    [Fact]
    public void Decode_BlankImage_FailsWithFiducialsNotFound()
    {
        using var blank = new SKBitmap(600, 400, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(blank))
        {
            canvas.Clear(SKColors.White);
        }

        var result = Decoder.Decode(blank);

        Assert.Equal(DecodeStatus.Undecodable, result.Status);
        Assert.Equal(DecodeFailureReason.FiducialsNotFound, result.FailureReason);
    }

    [Fact]
    public void TryProbe_ReadsBeaconAndHeader_WithoutPayloadWork()
    {
        using var evenFrame = RenderFrame(2, 5, DeterministicPayload(100), EccLevel.Medium);
        using var oddFrame = RenderFrame(3, 5, DeterministicPayload(100), EccLevel.Medium);

        var evenProbe = Decoder.TryProbe(evenFrame);
        var oddProbe = Decoder.TryProbe(oddFrame);

        Assert.True(evenProbe.Registered);
        Assert.True(evenProbe.BeaconIsBlack);
        Assert.Equal(2u, evenProbe.Header!.Value.FrameId);

        Assert.True(oddProbe.Registered);
        Assert.False(oddProbe.BeaconIsBlack);
        Assert.Equal(3u, oddProbe.Header!.Value.FrameId);
    }

    [Fact]
    public void TryProbe_BlankImage_NotRegistered()
    {
        using var blank = new SKBitmap(600, 400, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(blank))
        {
            canvas.Clear(SKColors.White);
        }

        var probe = Decoder.TryProbe(blank);

        Assert.False(probe.Registered);
        Assert.Null(probe.Header);
    }
}
