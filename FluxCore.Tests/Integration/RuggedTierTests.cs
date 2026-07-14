using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Integration;

/// <summary>
/// The rugged grayscale tier for lossy/RDP channels. A clean channel decodes it like any palette;
/// the point is that when the channel destroys chroma (the worst case: every pixel collapses to its
/// luma), the luma-separated rugged palette still decodes while the standard 8-colour palette — whose
/// entries rely on chroma to be distinct — does not.
/// </summary>
public class RuggedTierTests
{
    private const int Bits = 3;

    private static byte[] Deterministic(int length, int seed = 23)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    // Models a fully chroma-destroying channel: replace each pixel with its Rec.601 luma gray. Luma
    // is preserved (screen codecs keep it); all chroma is gone.
    private static void CollapseChroma(SKBitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                byte luma = (byte)Math.Round(0.299 * p.Red + 0.587 * p.Green + 0.114 * p.Blue);
                bitmap.SetPixel(x, y, new SKColor(luma, luma, luma, p.Alpha));
            }
    }

    private static FrameDecodeResult RenderAndDecode(PaletteKind kind, byte[] payload, bool collapseChroma)
    {
        var colorMap = ColorMap.FromCount(8, kind);
        var layout = new FrameLayout(
            FrameLayout.Default.GridWidthTiles, FrameLayout.Default.GridHeightTiles, FrameLayout.Default.TilePixelSize, Bits);
        var map = FrameEncoder.BuildFrame(1, 3, payload, EccLevel.Medium, Bits, layout);
        using var bitmap = SKBitmap.Decode(FrameRenderer.RenderPng(map, colorMap));
        if (collapseChroma)
            CollapseChroma(bitmap);
        return new FrameDecoder(colorMap).Decode(bitmap, bitsPerTile: Bits, layout: layout);
    }

    [Fact]
    public void Rugged8_RoundTrips_OnACleanChannel()
    {
        var payload = Deterministic(EccLevel.Medium.PayloadBytesPerFrame(FrameLayout.Default.CodewordsForBits(Bits)));

        var result = RenderAndDecode(PaletteKind.Rugged, payload, collapseChroma: false);

        Assert.Equal(DecodeStatus.Success, result.Status);
        Assert.Equal(payload, result.Payload);
    }

    [Fact]
    public void Rugged8_SurvivesTotalChromaLoss()
    {
        var payload = Deterministic(EccLevel.Medium.PayloadBytesPerFrame(FrameLayout.Default.CodewordsForBits(Bits)));

        var result = RenderAndDecode(PaletteKind.Rugged, payload, collapseChroma: true);

        Assert.Equal(DecodeStatus.Success, result.Status);
        Assert.Equal(payload, result.Payload);
    }

    [Fact]
    public void Standard8_FailsUnderTotalChromaLoss_WhereRuggedSucceeds()
    {
        var payload = Deterministic(EccLevel.Medium.PayloadBytesPerFrame(FrameLayout.Default.CodewordsForBits(Bits)));

        var standard = RenderAndDecode(PaletteKind.Standard, payload, collapseChroma: true);
        var rugged = RenderAndDecode(PaletteKind.Rugged, payload, collapseChroma: true);

        // Standard 8-colour depends on chroma to separate entries, so full chroma loss corrupts it.
        Assert.False(standard.Status == DecodeStatus.Success && standard.Payload is { } p && p.SequenceEqual(payload),
            "standard 8-colour unexpectedly survived total chroma loss");
        // The rugged grayscale tier is the point: it still decodes.
        Assert.Equal(DecodeStatus.Success, rugged.Status);
        Assert.Equal(payload, rugged.Payload);
    }
}
