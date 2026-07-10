using FluxCore.Decoding;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;
using FluxCore.Imaging;
using FluxCore.Transfer;
using Xunit;

namespace FluxCore.Tests.Transfer;

public class ReceptionHistoryServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ReceptionHistoryService _service = new();

    public ReceptionHistoryServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"flux_recv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string SessionRoot => Path.Combine(_root, "sessions");

    private static (MetadataPayload Metadata, List<(FrameHeader Header, byte[] Chunk)> Frames) BuildTransfer(
        int payloadLength, int seed = 11, string name = "recv.bin")
    {
        var payload = new byte[payloadLength];
        new Random(seed).NextBytes(payload);

        int perFrame = EccLevel.Medium.PayloadBytesPerFrame();
        uint payloadFrames = (uint)Math.Max(1, (payload.Length + perFrame - 1) / perFrame);
        uint total = payloadFrames + 1;

        var metadata = new MetadataPayload(
            Sha256Helper.ComputeHash(payload), PayloadType.Raw, EccLevel.Medium, total, payload.Length,
            name, payload.Length, new byte[32], 256);

        var frames = new List<(FrameHeader, byte[])>();
        for (uint id = 1; id <= payloadFrames; id++)
        {
            int offset = (int)(id - 1) * perFrame;
            int length = Math.Min(perFrame, payload.Length - offset);
            var chunk = payload[offset..(offset + length)];
            frames.Add((new FrameHeader(id, total, (ushort)length, Crc32Helper.ComputeChecksum(chunk), EccLevel.Medium), chunk));
        }

        return (metadata, frames);
    }

    [Fact]
    public void OpenAssembler_Fresh_PersistsAndIsResumable()
    {
        var (metadata, frames) = BuildTransfer(60_000);
        Assert.True(frames.Count >= 4);
        int half = frames.Count / 2;

        using (var first = _service.OpenAssembler(SessionRoot, metadata))
        {
            Assert.True(first.IsPersistent);
            Assert.Equal(0, first.ReceivedFrames);
            for (int i = 0; i < half; i++)
                first.AddFrame(frames[i].Header, frames[i].Chunk);
        }

        using var resumed = _service.OpenAssembler(SessionRoot, metadata);

        Assert.Equal(half, resumed.ReceivedFrames);
        Assert.False(resumed.IsComplete);
        Assert.Equal((uint)(half + 1), resumed.MissingFrameIds[0]);

        for (int i = half; i < frames.Count; i++)
            resumed.AddFrame(frames[i].Header, frames[i].Chunk);

        Assert.True(resumed.IsComplete);
        resumed.Verify(); // full payload across both sessions must hash-verify
    }

    [Fact]
    public void MarkComplete_DeletesBuffer_AndListsComplete()
    {
        var (metadata, frames) = BuildTransfer(20_000);
        string sessionDir;
        using (var asm = _service.OpenAssembler(SessionRoot, metadata))
        {
            foreach (var (header, chunk) in frames)
                asm.AddFrame(header, chunk);
            asm.Verify();
            sessionDir = Path.GetDirectoryName(asm.PayloadFilePath)!;
        }

        var saved = Path.Combine(_root, "output.bin");
        _service.MarkComplete(sessionDir, saved);

        Assert.False(File.Exists(Path.Combine(sessionDir, "payload.bin")));
        Assert.False(File.Exists(Path.Combine(sessionDir, "received.idx")));
        Assert.True(File.Exists(Path.Combine(sessionDir, "manifest.json")));

        var entry = Assert.Single(_service.List(SessionRoot));
        Assert.True(entry.IsComplete);
        Assert.Equal(saved, entry.SavedPath);
        Assert.Equal("recv.bin", entry.DisplayName);
        Assert.Equal((int)(metadata.TotalFrames - 1), entry.ReceivedFrames);
        Assert.NotNull(entry.CompletedUtc);
    }

    [Fact]
    public void List_InProgress_ReportsPartialCountAndNoSavedPath()
    {
        var (metadata, frames) = BuildTransfer(60_000);
        int received = 3;
        using (var asm = _service.OpenAssembler(SessionRoot, metadata))
        {
            for (int i = 0; i < received; i++)
                asm.AddFrame(frames[i].Header, frames[i].Chunk);
        }

        var entry = Assert.Single(_service.List(SessionRoot));
        Assert.False(entry.IsComplete);
        Assert.Equal(received, entry.ReceivedFrames);
        Assert.Null(entry.SavedPath);
        Assert.Null(entry.CompletedUtc);
    }

    [Fact]
    public void OpenAssembler_IncompatibleLeftover_StartsFresh()
    {
        // Same content signature (zeros) but a different payload length maps to the same folder yet is not resumable.
        var (metaA, framesA) = BuildTransfer(30_000, seed: 1);
        using (var a = _service.OpenAssembler(SessionRoot, metaA))
            a.AddFrame(framesA[0].Header, framesA[0].Chunk);

        var (metaB, _) = BuildTransfer(45_000, seed: 2);
        using var b = _service.OpenAssembler(SessionRoot, metaB);

        Assert.Equal(0, b.ReceivedFrames);
    }

    [Fact]
    public void Delete_RemovesSession()
    {
        var (metadata, frames) = BuildTransfer(15_000);
        using (var asm = _service.OpenAssembler(SessionRoot, metadata))
            asm.AddFrame(frames[0].Header, frames[0].Chunk);

        var entry = Assert.Single(_service.List(SessionRoot));
        _service.Delete(entry.SessionDirectory);

        Assert.Empty(_service.List(SessionRoot));
        Assert.False(Directory.Exists(entry.SessionDirectory));
    }

    [Fact]
    public void List_MissingRoot_ReturnsEmpty()
    {
        Assert.Empty(_service.List(Path.Combine(_root, "never_created")));
    }
}
