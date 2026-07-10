using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;
using FluxCore.Imaging;
using FluxCore.Transfer;
using SkiaSharp;
using Xunit;

namespace FluxCore.Tests.Transfer;

public class CaptureLoopServiceTests
{
    /// <summary>
    /// Renders a real transfer to frame bitmaps, then acts as both the screen (returning the
    /// current frame's rendered image) and the clicker (advancing to the next frame on click).
    /// Optional glitches let tests exercise dropped clicks and manual jumps.
    /// </summary>
    private sealed class FakeScreen : IScreenCapture, INextClicker
    {
        private readonly List<SKBitmap> _frames;
        private int _index;
        private int _ignoreClicks;
        private int _capturesAtEnd;

        public FakeScreen(List<SKBitmap> frames) => _frames = frames;

        public int ClickCount { get; private set; }

        /// <summary>Number of upcoming clicks to ignore (simulates RDP dropping a click).</summary>
        public int IgnoreNextClicks { get => _ignoreClicks; set => _ignoreClicks = value; }

        /// <summary>1-based click number that advances by two frames (simulates a skipped frame).</summary>
        public int SkipAtClick { get; set; } = -1;

        /// <summary>Frame index to "show" a few captures after parking at the last frame — simulates re-showing a gap during recovery.</summary>
        public int PresentWhenDone { get; set; } = -1;

        public SKBitmap Capture()
        {
            if (_index >= _frames.Count - 1)
            {
                _capturesAtEnd++;
                // Let the last frame be accepted first, then start "showing" the missing frame.
                if (PresentWhenDone >= 0 && _capturesAtEnd > 3)
                    return _frames[PresentWhenDone].Copy();
            }

            return _frames[_index].Copy();
        }

        public void ClickNext()
        {
            ClickCount++;
            if (_ignoreClicks > 0)
            {
                _ignoreClicks--;
                return;
            }

            int step = ClickCount == SkipAtClick ? 2 : 1;
            _index = Math.Min(_index + step, _frames.Count - 1);
        }

        public void JumpTo(int index) => _index = index;
    }

    private static (List<SKBitmap> Frames, byte[] Payload, MetadataPayload Metadata) BuildTransfer(
        int payloadLength, EccLevel level = EccLevel.Medium, int seed = 5)
    {
        var random = new Random(seed);
        var payload = new byte[payloadLength];
        random.NextBytes(payload);

        int perFrame = level.PayloadBytesPerFrame();
        uint payloadFrames = (uint)Math.Max(1, (payload.Length + perFrame - 1) / perFrame);
        uint total = payloadFrames + 1;

        var metadata = new MetadataPayload(
            Sha256Helper.ComputeHash(payload), PayloadType.Raw, level, total, payload.Length,
            "loop.bin", payload.Length, new byte[32], ColorMap.Default);

        var frames = new List<SKBitmap>
        {
            SKBitmap.Decode(FrameRenderer.RenderPng(FrameEncoder.BuildMetadataFrame(metadata.Serialize(), total), ColorMap.Default)),
        };
        for (uint id = 1; id <= payloadFrames; id++)
        {
            int offset = (int)(id - 1) * perFrame;
            int length = Math.Min(perFrame, payload.Length - offset);
            var map = FrameEncoder.BuildFrame(id, total, payload.AsSpan(offset, length), level);
            frames.Add(SKBitmap.Decode(FrameRenderer.RenderPng(map, ColorMap.Default)));
        }

        return (frames, payload, metadata);
    }

    private static CaptureLoopService CreateLoop(FakeScreen screen) =>
        new(screen, screen, ColorMap.Default,
            new CaptureLoopOptions(PollIntervalMs: 0, StabilityIntervalMs: 0, MaxPollsPerClick: 6, MaxReclicks: 3));

    private static Task<StallResolution> Abort(CancellationToken _) => Task.FromResult(StallResolution.Abort);

    /// <summary>Collects loop statuses synchronously (deterministic, unlike Progress&lt;T&gt;).</summary>
    private sealed class CollectingProgress : IProgress<LoopStatus>
    {
        public List<LoopStatus> Items { get; } = [];

        public void Report(LoopStatus value) => Items.Add(value);
    }

    [Fact]
    public async Task Run_CleanMultiFrameTransfer_CompletesAndVerifies()
    {
        var (frames, payload, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames);
        var loop = CreateLoop(screen);

        var report = await loop.RunAsync(null, Abort, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.NotNull(report.Assembler);
        Assert.Equal(payload, report.Assembler!.AssembleAndVerify());
        Assert.Equal(frames.Count - 1, report.FramesReceived);
        Assert.Equal(0, report.Reclicks);
    }

    [Fact]
    public async Task Run_DroppedClicks_RecoversByReclicking()
    {
        var (frames, payload, _) = BuildTransfer(20_000);
        var screen = new FakeScreen(frames) { IgnoreNextClicks = 2 };
        var loop = CreateLoop(screen);

        var report = await loop.RunAsync(null, Abort, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.Equal(payload, report.Assembler!.AssembleAndVerify());
        Assert.True(report.Reclicks > 0, "Dropped clicks should have forced at least one re-click.");
    }

    [Fact]
    public async Task Run_ClientStuck_StallsThenAbortsCleanly()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { IgnoreNextClicks = 1000 };
        var loop = CreateLoop(screen);

        var report = await loop.RunAsync(null, Abort, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Failed, report.State);
        Assert.True(report.Stalls >= 1);
        Assert.False(report.Assembler?.IsComplete ?? false);
    }

    [Fact]
    public async Task Run_StallThenRetryResolution_Completes()
    {
        var (frames, payload, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { IgnoreNextClicks = 5 };
        var loop = CreateLoop(screen);

        // First stall: stop ignoring clicks, then retry — the transfer should finish.
        Task<StallResolution> OnStall(CancellationToken _)
        {
            screen.IgnoreNextClicks = 0;
            return Task.FromResult(StallResolution.Retry);
        }

        var report = await loop.RunAsync(null, OnStall, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.Equal(payload, report.Assembler!.AssembleAndVerify());
        Assert.True(report.Stalls >= 1);
    }

    [Fact]
    public async Task Run_ManualJumpByUser_AcceptedAndResynced()
    {
        var (frames, payload, _) = BuildTransfer(30_000);
        var screen = new FakeScreen(frames);
        var loop = CreateLoop(screen);

        // After a moment, simulate the user clicking Client's Next themselves (jump ahead).
        // The loop must accept out-of-expected but valid frames; completeness is by set.
        var cts = new CancellationTokenSource();
        var runTask = loop.RunAsync(null, Abort, cts.Token);

        var report = await runTask;

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.Equal(payload, report.Assembler!.AssembleAndVerify());
    }

    [Fact]
    public async Task Run_SkippedFrame_RecoveredInGapPass()
    {
        // Click 2 skips a middle frame; the loop should recover it once it's re-shown, then verify.
        var (frames, payload, _) = BuildTransfer(40_000);
        var screen = new FakeScreen(frames) { SkipAtClick = 2, PresentWhenDone = 2 };
        var loop = CreateLoop(screen);

        var report = await loop.RunAsync(null, Abort, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.Equal(payload, report.Assembler!.AssembleAndVerify());
        Assert.Equal(frames.Count - 1, report.FramesReceived);
        Assert.Equal(0, report.Stalls);
    }

    [Fact]
    public async Task Run_SkippedFrame_ReportsRecoveringGapsWithMissingIds()
    {
        var (frames, _, _) = BuildTransfer(40_000);
        var screen = new FakeScreen(frames) { SkipAtClick = 2, PresentWhenDone = 2 };
        var loop = CreateLoop(screen);

        var progress = new CollectingProgress();
        var report = await loop.RunAsync(progress, Abort, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        var recovering = progress.Items.Where(s => s.State == CaptureLoopState.RecoveringGaps).ToList();
        Assert.NotEmpty(recovering);
        Assert.Contains(recovering, s => s.MissingFrameIds is { Count: > 0 } m && m.Contains(2u));
    }

    [Fact]
    public async Task Run_Cancellation_ReturnsCancelled()
    {
        var (frames, _, _) = BuildTransfer(200_000);
        var screen = new FakeScreen(frames) { IgnoreNextClicks = 100000 };
        var loop = new CaptureLoopService(screen, screen, ColorMap.Default,
            new CaptureLoopOptions(PollIntervalMs: 10, StabilityIntervalMs: 5));
        using var cts = new CancellationTokenSource();

        var task = loop.RunAsync(null, Abort, cts.Token);
        cts.CancelAfter(50);
        var report = await task;

        Assert.Equal(CaptureLoopState.Cancelled, report.State);
    }

    /// <summary>Seeds a persisting session with the first <paramref name="prefix"/> payload frames.</summary>
    private static (string Root, ReceptionHistoryService Service) SeedPartial(
        MetadataPayload metadata, byte[] payload, int prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"flux_resume_{Guid.NewGuid():N}", "sessions");
        var service = new ReceptionHistoryService();
        int perFrame = metadata.EccLevel.PayloadBytesPerFrame();

        using var seed = service.OpenAssembler(root, metadata);
        for (uint id = 1; id <= prefix; id++)
        {
            int offset = (int)(id - 1) * perFrame;
            int length = Math.Min(perFrame, payload.Length - offset);
            var chunk = payload[offset..(offset + length)];
            seed.AddFrame(new FrameHeader(id, metadata.TotalFrames, (ushort)length,
                Crc32Helper.ComputeChecksum(chunk), metadata.EccLevel), chunk);
        }

        return (root, service);
    }

    [Fact]
    public async Task Run_ResumeAutomatic_FastForwardsPastHeldFrames_AndCompletes()
    {
        var (frames, payload, metadata) = BuildTransfer(40_000);
        int prefix = (frames.Count - 1) / 2;
        var (root, service) = SeedPartial(metadata, payload, prefix);
        try
        {
            var screen = new FakeScreen(frames);
            var loop = new CaptureLoopService(screen, screen, ColorMap.Default,
                new CaptureLoopOptions(PollIntervalMs: 0, StabilityIntervalMs: 0, MaxPollsPerClick: 6, MaxReclicks: 3),
                assemblerFactory: m => service.OpenAssembler(root, m));

            Task<ResumeMode> OnResume(ResumeContext ctx, CancellationToken _)
            {
                Assert.Equal(prefix, ctx.ReceivedFrames);
                Assert.Equal((uint)(prefix + 1), ctx.FirstMissingFrameId);
                return Task.FromResult(ResumeMode.Automatic);
            }

            var report = await loop.RunAsync(null, Abort, CancellationToken.None, OnResume);

            Assert.Equal(CaptureLoopState.Complete, report.State);
            Assert.Equal(frames.Count - 1, report.Assembler!.ReceivedFrames);
            report.Assembler.Verify();
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Run_ResumeManual_CapturesUserShownFrame_AndCompletes()
    {
        var (frames, payload, metadata) = BuildTransfer(40_000);
        int prefix = (frames.Count - 1) / 2;
        var (root, service) = SeedPartial(metadata, payload, prefix);
        try
        {
            var screen = new FakeScreen(frames);
            var loop = new CaptureLoopService(screen, screen, ColorMap.Default,
                new CaptureLoopOptions(PollIntervalMs: 0, StabilityIntervalMs: 0, MaxPollsPerClick: 6, MaxReclicks: 3),
                assemblerFactory: m => service.OpenAssembler(root, m));

            // Manual: the user navigates the sender to the first missing frame, then continues.
            Task<ResumeMode> OnResume(ResumeContext ctx, CancellationToken _)
            {
                screen.JumpTo((int)ctx.FirstMissingFrameId);
                return Task.FromResult(ResumeMode.Manual);
            }

            var report = await loop.RunAsync(null, Abort, CancellationToken.None, OnResume);

            Assert.Equal(CaptureLoopState.Complete, report.State);
            report.Assembler!.Verify();
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Run_ResumeStartOver_DiscardsHeldFrames_AndRecapturesAll()
    {
        var (frames, payload, metadata) = BuildTransfer(40_000);
        int prefix = (frames.Count - 1) / 2;
        var (root, service) = SeedPartial(metadata, payload, prefix);
        try
        {
            var screen = new FakeScreen(frames);
            var loop = new CaptureLoopService(screen, screen, ColorMap.Default,
                new CaptureLoopOptions(PollIntervalMs: 0, StabilityIntervalMs: 0, MaxPollsPerClick: 6, MaxReclicks: 3),
                assemblerFactory: m => service.OpenAssembler(root, m));

            var report = await loop.RunAsync(null, Abort, CancellationToken.None,
                (_, _) => Task.FromResult(ResumeMode.StartOver));

            Assert.Equal(CaptureLoopState.Complete, report.State);
            Assert.Equal(frames.Count - 1, report.Assembler!.ReceivedFrames);
            report.Assembler.Verify();
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); } catch { }
        }
    }
}
