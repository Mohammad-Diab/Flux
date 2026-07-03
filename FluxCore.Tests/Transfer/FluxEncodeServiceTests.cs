using FluxCore.Compression;
using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Imaging;
using FluxCore.Transfer;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Transfer;

public class FluxEncodeServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FluxEncodeService _service = new(new CompressionService());

    public FluxEncodeServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"flux_enc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private async Task<string> CreateSourceFileAsync(int length, int seed = 5)
    {
        var random = new Random(seed);
        var data = new byte[length];
        random.NextBytes(data);
        var path = Path.Combine(_root, "source.bin");
        await File.WriteAllBytesAsync(path, data);
        return path;
    }

    private string SessionRoot => Path.Combine(_root, "sessions");

    [Fact]
    public async Task Encode_FreshSource_ProducesSessionWithAllFrames()
    {
        var source = await CreateSourceFileAsync(30_000);

        var result = await _service.EncodeAsync(source, SessionRoot, new EncodeOptions(Compress: false));

        Assert.False(result.PayloadReused);
        Assert.Equal((int)result.TotalFrames, result.FramesRendered);
        Assert.Equal(30_000, result.PayloadLength);
        Assert.True(File.Exists(Path.Combine(result.SessionDirectory, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(result.SessionDirectory, "payload.dat")));

        for (uint id = 0; id < result.TotalFrames; id++)
        {
            Assert.True(File.Exists(Path.Combine(result.FramesDirectory, FluxEncodeService.FrameFileName(id))));
        }
    }

    [Fact]
    public async Task Encode_SecondRun_ReusesPayloadAndRendersNothing()
    {
        var source = await CreateSourceFileAsync(30_000);
        var first = await _service.EncodeAsync(source, SessionRoot, new EncodeOptions());

        var second = await _service.EncodeAsync(source, SessionRoot, new EncodeOptions());

        Assert.True(second.PayloadReused);
        Assert.Equal(0, second.FramesRendered);
        Assert.Equal(first.SessionDirectory, second.SessionDirectory);
        Assert.Equal(first.TotalFrames, second.TotalFrames);
    }

    [Fact]
    public async Task Encode_MissingFrame_RerendersOnlyThatFrame()
    {
        var source = await CreateSourceFileAsync(30_000);
        var first = await _service.EncodeAsync(source, SessionRoot, new EncodeOptions(Compress: false));
        Assert.True(first.TotalFrames >= 3);

        var victim = Path.Combine(first.FramesDirectory, FluxEncodeService.FrameFileName(2));
        File.Delete(victim);

        var second = await _service.EncodeAsync(source, SessionRoot, new EncodeOptions(Compress: false));

        Assert.True(second.PayloadReused);
        Assert.Equal(1, second.FramesRendered);
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public async Task Encode_CancelledBeforeRender_ResumesCleanlyOnNextRun()
    {
        var source = await CreateSourceFileAsync(30_000);
        using var cts = new CancellationTokenSource();
        var cancelOnCompress = new InlineProgress(report =>
        {
            if (report.Phase == EncodePhase.Compressing)
                cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.EncodeAsync(source, SessionRoot, new EncodeOptions(), cancelOnCompress, cts.Token));

        var resumed = await _service.EncodeAsync(source, SessionRoot, new EncodeOptions());

        Assert.Equal((int)resumed.TotalFrames, resumed.FramesRendered);
        for (uint id = 0; id < resumed.TotalFrames; id++)
        {
            Assert.True(File.Exists(Path.Combine(resumed.FramesDirectory, FluxEncodeService.FrameFileName(id))));
        }
    }

    [Fact]
    public async Task Encode_ChangedContent_GetsFreshSessionDirectory()
    {
        var source = await CreateSourceFileAsync(20_000, seed: 1);
        var first = await _service.EncodeAsync(source, SessionRoot, new EncodeOptions());

        await File.WriteAllBytesAsync(source, new byte[20_000]);
        var second = await _service.EncodeAsync(source, SessionRoot, new EncodeOptions());

        Assert.NotEqual(first.SessionDirectory, second.SessionDirectory);
        Assert.False(second.PayloadReused);
    }

    [Fact]
    public async Task Encode_FolderSource_DecodesAndExtractsToIdenticalContent()
    {
        var folder = Path.Combine(_root, "docs");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        await File.WriteAllTextAsync(Path.Combine(folder, "readme.txt"), "hello flux transfer");
        await File.WriteAllBytesAsync(Path.Combine(folder, "nested", "data.bin"), new byte[5000]);

        var result = await _service.EncodeAsync(folder, SessionRoot, new EncodeOptions());

        var decoder = new FrameDecoder(ColorMap.Default);
        MetadataPayload? metadata = null;
        var frames = new List<(FrameHeader Header, byte[] Payload)>();

        for (uint id = 0; id < result.TotalFrames; id++)
        {
            using var bitmap = SKBitmap.Decode(
                Path.Combine(result.FramesDirectory, FluxEncodeService.FrameFileName(id)));
            var decode = decoder.Decode(bitmap);
            Assert.Equal(DecodeStatus.Success, decode.Status);

            if (id == 0)
                metadata = MetadataPayload.Deserialize(decode.Payload!);
            else
                frames.Add((decode.Header!.Value, decode.Payload!));
        }

        var assembler = new PayloadAssembler(metadata!);
        foreach (var (header, payload) in frames)
        {
            Assert.True(assembler.AddFrame(header, payload));
        }

        var extractDir = Path.Combine(_root, "extracted");
        await assembler.ExtractAsync(extractDir, new CompressionService());

        Assert.Equal("hello flux transfer", await File.ReadAllTextAsync(Path.Combine(extractDir, "readme.txt")));
        Assert.Equal(new byte[5000], await File.ReadAllBytesAsync(Path.Combine(extractDir, "nested", "data.bin")));
    }

    private sealed class InlineProgress(Action<EncodeProgress> handler) : IProgress<EncodeProgress>
    {
        public void Report(EncodeProgress value) => handler(value);
    }
}
