using FluxCore.Ecc;
using FluxCore.Transfer;
using Xunit;

namespace FluxCore.Tests.Transfer;

public class ContentSignatureTests : IDisposable
{
    private readonly string _root;

    public ContentSignatureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"flux_sig_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private async Task<string> WriteFileAsync(string name, string content)
    {
        var path = Path.Combine(_root, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    [Fact]
    public async Task PayloadSignature_IsStableAcrossRuns()
    {
        var path = await WriteFileAsync("a.txt", "stable content");

        var first = await ContentSignature.ComputePayloadSignatureAsync(path, compress: true);
        var second = await ContentSignature.ComputePayloadSignatureAsync(path, compress: true);

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
    }

    [Fact]
    public async Task PayloadSignature_ChangesWithContent()
    {
        var path = await WriteFileAsync("a.txt", "version one");
        var first = await ContentSignature.ComputePayloadSignatureAsync(path, compress: true);

        await File.WriteAllTextAsync(path, "version two");
        var second = await ContentSignature.ComputePayloadSignatureAsync(path, compress: true);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task PayloadSignature_ChangesWithName()
    {
        var pathA = await WriteFileAsync("a.txt", "same content");
        var pathB = await WriteFileAsync("b.txt", "same content");

        var first = await ContentSignature.ComputePayloadSignatureAsync(pathA, compress: true);
        var second = await ContentSignature.ComputePayloadSignatureAsync(pathB, compress: true);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task PayloadSignature_ChangesWithCompress()
    {
        var path = await WriteFileAsync("a.txt", "content");

        var compressed = await ContentSignature.ComputePayloadSignatureAsync(path, compress: true);
        var raw = await ContentSignature.ComputePayloadSignatureAsync(path, compress: false);

        Assert.NotEqual(compressed, raw);
    }

    [Fact]
    public void RenderSignature_ChangesWithEccGridTileColour_StableForSameSpec()
    {
        var baseline = ContentSignature.ComputeRenderSignature(new EncodeOptions());

        Assert.Equal(baseline, ContentSignature.ComputeRenderSignature(new EncodeOptions()));
        Assert.NotEqual(baseline, ContentSignature.ComputeRenderSignature(new EncodeOptions(EccLevel.High)));
        Assert.NotEqual(baseline, ContentSignature.ComputeRenderSignature(new EncodeOptions(GridWidthTiles: 240, GridHeightTiles: 135)));
        Assert.NotEqual(baseline, ContentSignature.ComputeRenderSignature(new EncodeOptions(TilePixelSize: 10)));
        Assert.NotEqual(baseline, ContentSignature.ComputeRenderSignature(new EncodeOptions(ColorCount: 512)));
    }

    [Fact]
    public async Task Combined_ChangesWithRender_ButPayloadStaysStable()
    {
        var path = await WriteFileAsync("a.txt", "content");
        var payloadA = await ContentSignature.ComputePayloadSignatureAsync(path, compress: true);
        var payloadB = await ContentSignature.ComputePayloadSignatureAsync(path, compress: true);

        // The payload signature is identical across two render specs of the same source.
        Assert.Equal(payloadA, payloadB);

        var renderA = ContentSignature.ComputeRenderSignature(new EncodeOptions());
        var renderB = ContentSignature.ComputeRenderSignature(new EncodeOptions(GridWidthTiles: 240, GridHeightTiles: 135));
        Assert.NotEqual(renderA, renderB);

        // The combined (wire) signature differs because the render differs.
        Assert.NotEqual(
            ContentSignature.Combine(payloadA, renderA),
            ContentSignature.Combine(payloadB, renderB));
    }

    [Fact]
    public async Task Folder_PayloadSignatureIsStable_AndChangesWithFileTimestamp()
    {
        var folder = Path.Combine(_root, "data");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        var filePath = Path.Combine(folder, "sub", "x.bin");
        await File.WriteAllBytesAsync(filePath, new byte[100]);

        var first = await ContentSignature.ComputePayloadSignatureAsync(folder, compress: true);
        var repeat = await ContentSignature.ComputePayloadSignatureAsync(folder, compress: true);
        Assert.Equal(first, repeat);

        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddDays(-1));
        var afterTouch = await ContentSignature.ComputePayloadSignatureAsync(folder, compress: true);
        Assert.NotEqual(first, afterTouch);
    }

    [Fact]
    public async Task MissingSource_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            ContentSignature.ComputePayloadSignatureAsync(Path.Combine(_root, "nope"), compress: true));
    }

    [Fact]
    public void ToSessionName_Is16LowercaseHexChars()
    {
        var signature = new byte[32];
        signature[0] = 0xAB;
        signature[7] = 0xCD;

        var name = ContentSignature.ToSessionName(signature);

        Assert.Equal(16, name.Length);
        Assert.Equal("ab000000000000cd", name);
    }
}
