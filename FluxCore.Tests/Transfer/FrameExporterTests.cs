using System.Text.Json;
using FluxCore.Ecc;
using FluxCore.Hashing;
using FluxCore.Transfer;
using Xunit;

namespace FluxCore.Tests.Transfer;

public class FrameExporterTests : IDisposable
{
    private readonly string _root;
    private readonly string _frames;
    private readonly string _dest;

    private static readonly FrameExportInfo Info = new(
        "photo.bin", SourceKind.File, TotalFrames: 3,
        GridWidthTiles: 160, GridHeightTiles: 90, EccLevel.Medium, ColorCount: 256,
        CreatedUtc: DateTimeOffset.UnixEpoch);

    private static readonly DateTimeOffset Exported = DateTimeOffset.UnixEpoch.AddHours(1);

    public FrameExporterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"flux_export_{Guid.NewGuid():N}");
        _frames = Path.Combine(_root, "frames");
        _dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(_frames);
        Directory.CreateDirectory(_dest);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void WriteFrames(int count)
    {
        for (uint i = 0; i < count; i++)
            File.WriteAllBytes(Path.Combine(_frames, FluxEncodeService.FrameFileName(i)), [(byte)i, 0x50, 0x4e, 0x47]);
        // A non-frame file must be ignored by the export.
        File.WriteAllText(Path.Combine(_frames, "notes.txt"), "ignore me");
    }

    [Fact]
    public void ExportToFolder_CopiesEveryFramePngAndNothingElse()
    {
        WriteFrames(3);

        var result = FrameExporter.ExportToFolder(_frames, _dest, "photo.bin (160x90)", Info, Exported);

        Assert.Equal(3, result.FrameCount);
        var copied = Directory.GetFiles(result.OutputDirectory, "frame_*.png");
        Assert.Equal(3, copied.Length);
        Assert.False(File.Exists(Path.Combine(result.OutputDirectory, "notes.txt")));
    }

    [Fact]
    public void ExportToFolder_WritesManifestWithMatchingPerFrameChecksums()
    {
        WriteFrames(2);

        var result = FrameExporter.ExportToFolder(_frames, _dest, "photo.bin", Info, Exported);

        var manifestPath = Path.Combine(result.OutputDirectory, FrameExporter.ManifestFileName);
        Assert.True(File.Exists(manifestPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;
        Assert.Equal("photo.bin", root.GetProperty("DisplayName").GetString());
        Assert.Equal("Medium", root.GetProperty("EccLevel").GetString());
        Assert.Equal(256, root.GetProperty("ColorCount").GetInt32());

        var frames = root.GetProperty("Frames").EnumerateArray().ToArray();
        Assert.Equal(2, frames.Length);
        foreach (var frame in frames)
        {
            var name = frame.GetProperty("File").GetString()!;
            var expected = Sha256Helper.ToHexString(
                Sha256Helper.ComputeHash(File.ReadAllBytes(Path.Combine(result.OutputDirectory, name))));
            Assert.Equal(expected, frame.GetProperty("Sha256").GetString());
        }
    }

    [Fact]
    public void ExportToFolder_UniquifiesInsteadOfClobbering()
    {
        WriteFrames(1);

        var first = FrameExporter.ExportToFolder(_frames, _dest, "photo", Info, Exported);
        var second = FrameExporter.ExportToFolder(_frames, _dest, "photo", Info, Exported);

        Assert.NotEqual(first.OutputDirectory, second.OutputDirectory);
        Assert.EndsWith("(2)", Path.GetFileName(second.OutputDirectory));
        Assert.True(Directory.Exists(first.OutputDirectory));
    }

    [Fact]
    public void ExportToFolder_ThrowsWhenNoFrames()
    {
        Assert.Throws<InvalidOperationException>(
            () => FrameExporter.ExportToFolder(_frames, _dest, "empty", Info, Exported));
    }

    [Fact]
    public void ExportToFolder_ThrowsWhenFramesDirectoryMissing()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => FrameExporter.ExportToFolder(Path.Combine(_root, "nope"), _dest, "x", Info, Exported));
    }
}
