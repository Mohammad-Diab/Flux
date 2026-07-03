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

    private static readonly EncodeOptions DefaultOptions = new();

    [Fact]
    public async Task File_SignatureIsStableAcrossRuns()
    {
        var path = Path.Combine(_root, "a.txt");
        await File.WriteAllTextAsync(path, "stable content");

        var first = await ContentSignature.ComputeAsync(path, DefaultOptions);
        var second = await ContentSignature.ComputeAsync(path, DefaultOptions);

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
    }

    [Fact]
    public async Task File_SignatureChangesWithContent()
    {
        var path = Path.Combine(_root, "a.txt");
        await File.WriteAllTextAsync(path, "version one");
        var first = await ContentSignature.ComputeAsync(path, DefaultOptions);

        await File.WriteAllTextAsync(path, "version two");
        var second = await ContentSignature.ComputeAsync(path, DefaultOptions);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task File_SignatureChangesWithName()
    {
        var pathA = Path.Combine(_root, "a.txt");
        var pathB = Path.Combine(_root, "b.txt");
        await File.WriteAllTextAsync(pathA, "same content");
        await File.WriteAllTextAsync(pathB, "same content");

        var first = await ContentSignature.ComputeAsync(pathA, DefaultOptions);
        var second = await ContentSignature.ComputeAsync(pathB, DefaultOptions);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Signature_ChangesWithEncodeOptions()
    {
        var path = Path.Combine(_root, "a.txt");
        await File.WriteAllTextAsync(path, "content");

        var medium = await ContentSignature.ComputeAsync(path, new EncodeOptions(EccLevel.Medium));
        var high = await ContentSignature.ComputeAsync(path, new EncodeOptions(EccLevel.High));
        var raw = await ContentSignature.ComputeAsync(path, new EncodeOptions(EccLevel.Medium, Compress: false));

        Assert.NotEqual(medium, high);
        Assert.NotEqual(medium, raw);
    }

    [Fact]
    public async Task Folder_SignatureIsStable_AndChangesWithFileTimestamp()
    {
        var folder = Path.Combine(_root, "data");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        var filePath = Path.Combine(folder, "sub", "x.bin");
        await File.WriteAllBytesAsync(filePath, new byte[100]);

        var first = await ContentSignature.ComputeAsync(folder, DefaultOptions);
        var repeat = await ContentSignature.ComputeAsync(folder, DefaultOptions);
        Assert.Equal(first, repeat);

        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddDays(-1));
        var afterTouch = await ContentSignature.ComputeAsync(folder, DefaultOptions);
        Assert.NotEqual(first, afterTouch);
    }

    [Fact]
    public async Task MissingSource_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            ContentSignature.ComputeAsync(Path.Combine(_root, "nope"), DefaultOptions));
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
