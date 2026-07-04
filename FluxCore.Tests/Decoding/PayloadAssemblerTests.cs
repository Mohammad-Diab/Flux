using FluxCore.Compression;
using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;
using FluxCore.Imaging;
using Xunit;

namespace FluxCore.Tests.Decoding;

public class PayloadAssemblerTests
{
    private static byte[] DeterministicPayload(int length, int seed = 42)
    {
        var random = new Random(seed);
        var payload = new byte[length];
        random.NextBytes(payload);
        return payload;
    }

    private static (MetadataPayload Metadata, byte[] Payload, List<(FrameHeader Header, byte[] Chunk)> Frames)
        CreateTransfer(int payloadLength, EccLevel level = EccLevel.Medium, PayloadType type = PayloadType.Raw)
    {
        var payload = DeterministicPayload(payloadLength);
        int bytesPerFrame = level.PayloadBytesPerFrame();
        uint payloadFrames = (uint)Math.Max(1, (payload.Length + bytesPerFrame - 1) / bytesPerFrame);
        uint totalFrames = payloadFrames + 1;

        var metadata = new MetadataPayload(
            Sha256Helper.ComputeHash(payload), type, level, totalFrames, payload.Length,
            "asm-test.bin", payload.Length, new byte[32], ColorMap.Default);

        var frames = new List<(FrameHeader, byte[])>();
        for (uint id = 1; id <= payloadFrames; id++)
        {
            int offset = (int)(id - 1) * bytesPerFrame;
            int length = Math.Min(bytesPerFrame, payload.Length - offset);
            var chunk = payload[offset..(offset + length)];
            frames.Add((new FrameHeader(id, totalFrames, (ushort)length, Crc32Helper.ComputeChecksum(chunk), level), chunk));
        }

        return (metadata, payload, frames);
    }

    [Fact]
    public void AddFrames_OutOfOrder_AssemblesCorrectly()
    {
        var (metadata, payload, frames) = CreateTransfer(25_000);
        var assembler = new PayloadAssembler(metadata);

        foreach (var (header, chunk) in frames.AsEnumerable().Reverse())
        {
            Assert.True(assembler.AddFrame(header, chunk));
        }

        Assert.True(assembler.IsComplete);
        Assert.Equal(payload, assembler.AssembleAndVerify());
    }

    [Fact]
    public void AddFrame_Duplicate_ReturnsFalse_AndDoesNotBreakAssembly()
    {
        var (metadata, payload, frames) = CreateTransfer(15_000);
        var assembler = new PayloadAssembler(metadata);

        foreach (var (header, chunk) in frames)
        {
            Assert.True(assembler.AddFrame(header, chunk));
            Assert.False(assembler.AddFrame(header, chunk));
        }

        Assert.Equal(payload, assembler.AssembleAndVerify());
    }

    [Fact]
    public void MissingFrameIds_ReportsGaps_AndAssembleThrowsWhileIncomplete()
    {
        var (metadata, _, frames) = CreateTransfer(25_000);
        Assert.True(frames.Count >= 3);
        var assembler = new PayloadAssembler(metadata);

        assembler.AddFrame(frames[0].Header, frames[0].Chunk);
        assembler.AddFrame(frames[2].Header, frames[2].Chunk);

        Assert.False(assembler.IsComplete);
        Assert.Equal([2u], assembler.MissingFrameIds);
        Assert.Throws<InvalidOperationException>(() => assembler.AssembleAndVerify());
    }

    [Fact]
    public void AssembleAndVerify_TamperedFrame_ThrowsInvalidData()
    {
        var (metadata, _, frames) = CreateTransfer(15_000);
        var assembler = new PayloadAssembler(metadata);

        foreach (var (header, chunk) in frames)
        {
            var copy = chunk.ToArray();
            if (header.FrameId == 1)
                copy[0] ^= 0xFF;
            assembler.AddFrame(header, copy);
        }

        Assert.Throws<InvalidDataException>(() => assembler.AssembleAndVerify());
    }

    [Fact]
    public void AddFrame_InconsistentFrames_Throw()
    {
        var (metadata, _, frames) = CreateTransfer(15_000);
        var assembler = new PayloadAssembler(metadata);
        var (header, chunk) = frames[0];

        var wrongTotal = new FrameHeader(1, metadata.TotalFrames + 1, (ushort)chunk.Length, 0, metadata.EccLevel);
        Assert.Throws<ArgumentException>(() => assembler.AddFrame(wrongTotal, chunk));

        var frameZero = new FrameHeader(0, metadata.TotalFrames, (ushort)chunk.Length, 0, metadata.EccLevel);
        Assert.Throws<ArgumentException>(() => assembler.AddFrame(frameZero, chunk));

        Assert.Throws<ArgumentException>(() => assembler.AddFrame(header, chunk[..^1]));
    }

    [Fact]
    public async Task DiskBacked_OutOfOrder_VerifiesAndExtractsRaw()
    {
        var (metadata, payload, frames) = CreateTransfer(25_000);
        using var assembler = new PayloadAssembler(metadata, diskThresholdBytes: 0);

        Assert.True(assembler.IsDiskBacked);

        foreach (var (header, chunk) in frames.AsEnumerable().Reverse())
        {
            Assert.True(assembler.AddFrame(header, chunk));
        }

        Assert.True(assembler.IsComplete);
        assembler.Verify(); // streaming SHA over the temp file — must not throw

        var target = Path.Combine(Path.GetTempPath(), $"flux_asm_{Guid.NewGuid():N}");
        try
        {
            var extracted = await assembler.ExtractAsync(target, new CompressionService());
            Assert.Equal(Path.Combine(target, "asm-test.bin"), extracted);
            Assert.Equal(payload, await File.ReadAllBytesAsync(extracted));
        }
        finally
        {
            try { Directory.Delete(target, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DiskBacked_TamperedFrame_VerifyThrowsInvalidData()
    {
        var (metadata, _, frames) = CreateTransfer(15_000);
        using var assembler = new PayloadAssembler(metadata, diskThresholdBytes: 0);

        foreach (var (header, chunk) in frames)
        {
            var copy = chunk.ToArray();
            if (header.FrameId == 1)
                copy[0] ^= 0xFF;
            assembler.AddFrame(header, copy);
        }

        Assert.Throws<InvalidDataException>(() => assembler.Verify());
    }

    [Fact]
    public void DiskBacked_AssembleAndVerify_Throws()
    {
        var (metadata, _, frames) = CreateTransfer(5_000);
        using var assembler = new PayloadAssembler(metadata, diskThresholdBytes: 0);
        foreach (var (header, chunk) in frames)
        {
            assembler.AddFrame(header, chunk);
        }

        Assert.Throws<InvalidOperationException>(() => assembler.AssembleAndVerify());
    }

    [Fact]
    public async Task ExtractAsync_RawPayload_WritesOriginalFileName()
    {
        var (metadata, payload, frames) = CreateTransfer(5_000);
        var assembler = new PayloadAssembler(metadata);
        foreach (var (header, chunk) in frames)
        {
            assembler.AddFrame(header, chunk);
        }

        var target = Path.Combine(Path.GetTempPath(), $"flux_asm_{Guid.NewGuid():N}");
        try
        {
            var extracted = await assembler.ExtractAsync(target, new CompressionService());

            Assert.Equal(Path.Combine(target, "asm-test.bin"), extracted);
            Assert.Equal(payload, await File.ReadAllBytesAsync(extracted));
        }
        finally
        {
            try { Directory.Delete(target, recursive: true); } catch { }
        }
    }
}
