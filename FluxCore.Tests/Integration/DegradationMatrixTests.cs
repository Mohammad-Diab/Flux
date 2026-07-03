using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Integration;

/// <summary>
/// Milestone A degradation matrix: frames re-rendered through scaling, JPEG recompression,
/// and offset must either decode byte-perfectly or fail cleanly — never silently corrupt.
/// Pins which ECC level survives which distortion.
/// </summary>
public class DegradationMatrixTests
{
    private static SKBitmap Degrade(byte[] png, double scale, int jpegQuality, int offsetX, int offsetY)
    {
        using var source = SKBitmap.Decode(png);

        var scaledInfo = new SKImageInfo(
            (int)Math.Round(source.Width * scale),
            (int)Math.Round(source.Height * scale),
            SKColorType.Rgba8888);
        using var scaled = source.Resize(scaledInfo, SKFilterQuality.High)!;

        using var padded = new SKBitmap(
            scaled.Width + offsetX + 25, scaled.Height + offsetY + 25, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(padded))
        {
            canvas.Clear(new SKColor(215, 215, 215));
            canvas.DrawBitmap(scaled, offsetX, offsetY);
        }

        using var image = SKImage.FromBitmap(padded);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, jpegQuality);
        return SKBitmap.Decode(jpeg.ToArray());
    }

    private static (bool Decoded, byte[]? Payload, FrameDecodeResult Result) DecodeDegraded(
        byte[] payload, EccLevel level, double scale, int jpegQuality)
    {
        var map = FrameEncoder.BuildFrame(1, 2, payload, level);
        var png = FrameRenderer.RenderPng(map, ColorMap.Default);

        using var degraded = Degrade(png, scale, jpegQuality, offsetX: 17, offsetY: 23);
        var decoder = new FrameDecoder(ColorMap.Default);
        var result = decoder.Decode(degraded);

        return (result.Status == DecodeStatus.Success, result.Payload, result);
    }

    [Theory]
    [InlineData(0.8, 85)]
    [InlineData(1.0, 85)]
    [InlineData(1.25, 85)]
    public void Medium_SurvivesJpeg85_AtAllScales(double scale, int quality)
    {
        var payload = GoldenRoundTripTests.DeterministicPayload(EccLevel.Medium.PayloadBytesPerFrame());

        var (decoded, recovered, result) = DecodeDegraded(payload, EccLevel.Medium, scale, quality);

        Assert.True(decoded,
            $"Medium must survive JPEG q{quality} at {scale}x. Got {result.Status}/{result.FailureReason}, " +
            $"lowConf={result.Diagnostics.LowConfidenceDataTiles}, meanDist={result.Diagnostics.MeanPaletteDistance:F1}");
        Assert.Equal(payload, recovered);
    }

    [Theory]
    [InlineData(0.8)]
    [InlineData(1.0)]
    [InlineData(1.25)]
    public void High_SurvivesJpeg75_AtAllScales(double scale)
    {
        var payload = GoldenRoundTripTests.DeterministicPayload(EccLevel.High.PayloadBytesPerFrame());

        var (decoded, recovered, result) = DecodeDegraded(payload, EccLevel.High, scale, 75);

        Assert.True(decoded,
            $"High must survive JPEG q75 at {scale}x. Got {result.Status}/{result.FailureReason}, " +
            $"lowConf={result.Diagnostics.LowConfidenceDataTiles}, meanDist={result.Diagnostics.MeanPaletteDistance:F1}");
        Assert.Equal(payload, recovered);
    }

    [Theory]
    [InlineData(0.8, 75)]
    [InlineData(1.0, 75)]
    [InlineData(1.25, 75)]
    public void Medium_AtJpeg75_NeverSilentlyCorrupts(double scale, int quality)
    {
        var payload = GoldenRoundTripTests.DeterministicPayload(EccLevel.Medium.PayloadBytesPerFrame());

        var (decoded, recovered, _) = DecodeDegraded(payload, EccLevel.Medium, scale, quality);

        if (decoded)
        {
            Assert.Equal(payload, recovered);
        }
    }

    [Theory]
    [InlineData(30)]
    [InlineData(15)]
    public void ExtremeJpeg_FailsCleanly_NeverSilentlyCorrupts(int quality)
    {
        var payload = GoldenRoundTripTests.DeterministicPayload(EccLevel.Medium.PayloadBytesPerFrame());

        var (decoded, recovered, _) = DecodeDegraded(payload, EccLevel.Medium, 1.0, quality);

        if (decoded)
        {
            Assert.Equal(payload, recovered);
        }
    }

    [Fact]
    public void FullTransfer_ThroughJpeg85_RoundTripsWithShaVerification()
    {
        var payload = GoldenRoundTripTests.DeterministicPayload(15_000);
        var pngs = GoldenRoundTripTests.EncodeTransfer(payload, EccLevel.Medium, "degraded.bin");

        var (_, decoded) = GoldenRoundTripTests.DecodeTransfer(
            pngs, png => Degrade(png, 1.1, 85, offsetX: 12, offsetY: 9));

        Assert.Equal(payload, decoded);
    }
}
