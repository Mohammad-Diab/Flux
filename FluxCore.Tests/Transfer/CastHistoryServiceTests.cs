using FluxCore.Compression;
using FluxCore.Transfer;
using Xunit;

namespace FluxCore.Tests.Transfer;

public class CastHistoryServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FluxEncodeService _encode = new(new CompressionService());
    private readonly CastHistoryService _history = new();

    public CastHistoryServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"flux_hist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string SessionRoot => Path.Combine(_root, "sessions");

    private async Task<string> CreateFileAsync(string name, int length)
    {
        var data = new byte[length];
        new Random(7).NextBytes(data);
        var path = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(path, data);
        return path;
    }

    [Fact]
    public async Task List_AfterEncodingFile_ReportsEnrichedEntry()
    {
        var source = await CreateFileAsync("photo.bin", 40_000);
        var result = await _encode.EncodeAsync(source, SessionRoot, new EncodeOptions(Compress: false));

        var entries = _history.List(SessionRoot);

        var entry = Assert.Single(entries);
        Assert.Equal("photo.bin", entry.DisplayName);
        Assert.Equal(SourceKind.File, entry.SourceKind);
        Assert.Equal(source, entry.SourcePath);
        Assert.Equal(result.TotalFrames, entry.TotalFrames);
        Assert.Equal(result.PayloadLength, entry.PayloadLength);
        Assert.True(entry.IsComplete);
        Assert.True(DateTimeOffset.UtcNow - entry.CreatedUtc < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task List_AfterEncodingFolder_MarksSourceKindFolder()
    {
        var folder = Path.Combine(_root, "docs");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "hello");

        await _encode.EncodeAsync(folder, SessionRoot, new EncodeOptions());

        var entry = Assert.Single(_history.List(SessionRoot));
        Assert.Equal("docs", entry.DisplayName);
        Assert.Equal(SourceKind.Folder, entry.SourceKind);
    }

    [Fact]
    public async Task List_MissingRoot_ReturnsEmpty()
    {
        var entries = _history.List(Path.Combine(_root, "never_created"));
        Assert.Empty(entries);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task OpenForPresenting_ReconstructsSessionFromDisk()
    {
        var source = await CreateFileAsync("clip.bin", 40_000);
        var encoded = await _encode.EncodeAsync(source, SessionRoot, new EncodeOptions(Compress: false));
        var entry = Assert.Single(_history.List(SessionRoot));

        var reopened = _history.OpenForPresenting(entry.SessionDirectory);

        Assert.Equal(encoded.TotalFrames, reopened.TotalFrames);
        Assert.Equal(encoded.FramesDirectory, reopened.FramesDirectory);
        Assert.True(File.Exists(Path.Combine(reopened.FramesDirectory, FluxEncodeService.FrameFileName(0))));
    }

    [Fact]
    public async Task Delete_RemovesSessionFromList()
    {
        var source = await CreateFileAsync("gone.bin", 20_000);
        await _encode.EncodeAsync(source, SessionRoot, new EncodeOptions(Compress: false));
        var entry = Assert.Single(_history.List(SessionRoot));

        _history.Delete(entry.SessionDirectory);

        Assert.Empty(_history.List(SessionRoot));
        Assert.False(Directory.Exists(entry.SessionDirectory));
    }
}
