using System.Diagnostics;
using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;
using FluxCore.Imaging;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FluxCore.Tests.Transfer;

/// <summary>
/// Pins the cost of the frame-0 hunt: during acquisition the receiver decodes a full-window
/// capture on every poll, so a slow decode directly stretches the "Looking for the first frame"
/// phase the user sits through.
/// </summary>
public class Frame0AcquisitionPerfTests
{
    private readonly ITestOutputHelper _output;

    public Frame0AcquisitionPerfTests(ITestOutputHelper output) => _output = output;

    private static SKBitmap RenderFrame0InWindow(int windowWidth, int windowHeight)
    {
        var metadata = new MetadataPayload(
            Sha256Helper.ComputeHash([1, 2, 3]), PayloadType.Raw, EccLevel.Medium, 10, 3,
            "perf.bin", 3, new byte[32], 256);
        using var frame = SKBitmap.Decode(
            FrameRenderer.RenderPng(FrameEncoder.BuildMetadataFrame(metadata.Serialize(), 10), ColorMap.Default));

        var window = new SKBitmap(windowWidth, windowHeight);
        using var canvas = new SKCanvas(window);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(frame, (windowWidth - frame.Width) / 2f, (windowHeight - frame.Height) / 2f);
        return window;
    }

    [Fact]
    public void DecodeMetadataFrame_FullWindowCapture_IsFastEnoughToPoll()
    {
        using var window = RenderFrame0InWindow(1920, 1080);
        var decoder = new FrameDecoder(ColorMap.Default);

        // Warm-up decode (JIT, first-touch allocations), then measure a handful.
        Assert.Equal(DecodeStatus.Success, decoder.DecodeMetadataFrame(window).Status);

        const int rounds = 5;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < rounds; i++)
        {
            Assert.Equal(DecodeStatus.Success, decoder.DecodeMetadataFrame(window).Status);
        }

        watch.Stop();
        long perDecode = watch.ElapsedMilliseconds / rounds;
        _output.WriteLine($"Frame-0 decode on a 1920×1080 capture: {perDecode} ms/decode");

        // Polling at ~10 Hz needs decode well under the interval; 250 ms is a generous CI bound.
        Assert.True(perDecode < 250, $"Frame-0 decode took {perDecode} ms — the frame-0 hunt will feel stuck.");
    }

    [Fact]
    public void DecodeMetadataFrame_EmptyWindowCapture_FailsFast()
    {
        using var window = new SKBitmap(1920, 1080);
        window.Erase(SKColors.White);
        var decoder = new FrameDecoder(ColorMap.Default);

        decoder.DecodeMetadataFrame(window);   // warm-up

        const int rounds = 5;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < rounds; i++)
        {
            Assert.NotEqual(DecodeStatus.Success, decoder.DecodeMetadataFrame(window).Status);
        }

        watch.Stop();
        long perDecode = watch.ElapsedMilliseconds / rounds;
        _output.WriteLine($"Frame-0 miss on an empty 1920×1080 capture: {perDecode} ms/attempt");
        Assert.True(perDecode < 250, $"A frame-0 miss took {perDecode} ms — empty polls must reject quickly.");
    }
}
