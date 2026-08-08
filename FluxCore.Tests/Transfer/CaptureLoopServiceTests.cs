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
    /// Optional glitches let tests exercise dropped clicks, blocked buttons, damaged channels,
    /// and manual jumps.
    /// </summary>
    private sealed class FakeScreen : IScreenCapture, INextClicker
    {
        private readonly List<SKBitmap> _frames;
        private readonly Dictionary<int, SKBitmap> _damaged = [];
        private SKBitmap? _blank;
        private int _index;
        private int _ignoreClicks;
        private int _capturesAtEnd;

        public FakeScreen(List<SKBitmap> frames) => _frames = frames;

        public int ClickCount { get; private set; }

        /// <summary>Number of upcoming clicks to ignore (simulates RDP dropping a click).</summary>
        public int IgnoreNextClicks { get => _ignoreClicks; set => _ignoreClicks = value; }

        /// <summary>1-based click number that advances by two frames (simulates a skipped frame).</summary>
        public int SkipAtClick { get; set; } = -1;

        /// <summary>Number of upcoming click attempts to refuse as blocked (simulates a covering window).</summary>
        public int BlockNextClicks { get; set; }

        /// <summary>Number of upcoming click attempts to refuse as WindowGone (simulates a closed sender).</summary>
        public int GoneNextClicks { get; set; }

        /// <summary>1-based click attempt that throws (simulates a transient error mid-transfer).</summary>
        public int ThrowAtClick { get; set; } = -1;

        /// <summary>After this many delivered clicks, captures show a damaged frame (channel gone bad).</summary>
        public int DamageAfterClick { get; set; } = -1;

        /// <summary>After this many delivered clicks, captures show a blank screen (frame gone).</summary>
        public int BlankAfterClick { get; set; } = -1;

        private int _clickAttempts;

        /// <summary>Frame index to "show" a few captures after parking at the last frame — simulates re-showing a gap during recovery.</summary>
        public int PresentWhenDone { get; set; } = -1;

        public SKBitmap Capture()
        {
            if (BlankAfterClick >= 0 && ClickCount >= BlankAfterClick)
                return Blank().Copy();
            if (DamageAfterClick >= 0 && ClickCount >= DamageAfterClick)
                return Damaged(_index).Copy();

            if (_index >= _frames.Count - 1)
            {
                _capturesAtEnd++;
                // Let the last frame be accepted first, then start "showing" the missing frame.
                if (PresentWhenDone >= 0 && _capturesAtEnd > 3)
                    return _frames[PresentWhenDone].Copy();
            }

            return _frames[_index].Copy();
        }

        public NextClickOutcome ClickNext()
        {
            if (BlockNextClicks > 0)
            {
                BlockNextClicks--;
                return NextClickOutcome.Covered;
            }

            if (GoneNextClicks > 0)
            {
                GoneNextClicks--;
                return NextClickOutcome.WindowGone;
            }

            if (++_clickAttempts == ThrowAtClick)
                throw new IOException("click failed");

            ClickCount++;
            if (_ignoreClicks > 0)
            {
                _ignoreClicks--;
                return NextClickOutcome.Clicked;
            }

            int step = ClickCount == SkipAtClick ? 2 : 1;
            _index = Math.Min(_index + step, _frames.Count - 1);
            return NextClickOutcome.Clicked;
        }

        public void JumpTo(int index) => _index = index;

        private SKBitmap Blank()
        {
            if (_blank is null)
            {
                _blank = new SKBitmap(_frames[0].Width, _frames[0].Height);
                _blank.Erase(SKColors.White);
            }

            return _blank;
        }

        // Deterministic per frame so the stability gate (two identical captures) still passes:
        // noise over the central half of the frame — fiducials and timing at the edges survive,
        // so it registers but is far beyond ECC repair.
        private SKBitmap Damaged(int index)
        {
            if (_damaged.TryGetValue(index, out var cached))
                return cached;

            var copy = _frames[index].Copy();
            var random = new Random(index * 977 + 13);
            int x0 = copy.Width / 4, x1 = copy.Width * 3 / 4;
            int y0 = copy.Height / 4, y1 = copy.Height * 3 / 4;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    copy.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
                }
            }

            _damaged[index] = copy;
            return copy;
        }
    }

    private sealed class FakeRecalibrator : ILoopRecalibrator
    {
        public bool NextButtonFindable { get; set; } = true;

        public bool FrameFindable { get; set; } = true;

        public int NextButtonCalls { get; private set; }

        public int FrameCalls { get; private set; }

        public Task<bool> RecalibrateNextButtonAsync(CancellationToken cancellationToken)
        {
            NextButtonCalls++;
            return Task.FromResult(NextButtonFindable);
        }

        public Task<bool> RecalibrateFrameAsync(CancellationToken cancellationToken)
        {
            FrameCalls++;
            return Task.FromResult(FrameFindable);
        }
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
            "loop.bin", payload.Length, new byte[32], 256);

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

    private static CaptureLoopService CreateLoop(FakeScreen screen, ILoopRecalibrator? recalibrator = null) =>
        new(screen, screen, ColorMap.Default,
            new CaptureLoopOptions(PollIntervalMs: 0, StabilityIntervalMs: 0, MaxPollsPerClick: 6, MaxReclicks: 3, BlockedRetryIntervalMs: 0),
            recalibrator: recalibrator);

    private static Task<StallResolution> Stop(StallContext _, CancellationToken __) =>
        Task.FromResult(StallResolution.Stop);

    /// <summary>Collects loop statuses synchronously (deterministic, unlike Progress&lt;T&gt;).</summary>
    private sealed class CollectingProgress : IProgress<LoopStatus>
    {
        public List<LoopStatus> Items { get; } = [];

        public void Report(LoopStatus value) => Items.Add(value);
    }

    [Fact]
    public async Task Run_TransientErrorMidTransfer_UserRetries_Completes()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { ThrowAtClick = 2 };
        var loop = CreateLoop(screen);
        var progress = new CollectingProgress();
        var causes = new List<StallCause>();
        Task<StallResolution> Retry(StallContext context, CancellationToken _)
        {
            causes.Add(context.Cause);
            return Task.FromResult(StallResolution.Retry);
        }

        var report = await loop.RunAsync(progress, Retry, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.Equal([StallCause.Error], causes);
        Assert.Contains(progress.Items, s => s.State == CaptureLoopState.Stalled && s.Message.Contains("click failed"));
    }

    [Fact]
    public async Task Run_TransientErrorMidTransfer_UserStops_ReportsTheError()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { ThrowAtClick = 2 };
        var loop = CreateLoop(screen);

        var report = await loop.RunAsync(null, Stop, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Stopped, report.State);
        Assert.Contains("click failed", report.Error);
    }

    [Fact]
    public async Task Run_BrieflyBlockedNextButton_RetriesWithoutClicking_ThenCompletes()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { BlockNextClicks = 2 };
        var loop = CreateLoop(screen);
        var progress = new CollectingProgress();

        var report = await loop.RunAsync(progress, Stop, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        var blocked = progress.Items.Where(s => s.State == CaptureLoopState.ChannelBlocked).ToList();
        Assert.NotEmpty(blocked);
        Assert.Contains("covering", blocked[0].Message);
        Assert.Contains(progress.Items, s => s.Message.Contains("reachable again"));
    }

    [Fact]
    public async Task Run_PersistentlyBlockedNextButton_StallsWithCause_RetryAfterClearingCompletes()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { BlockNextClicks = 1000 };
        var loop = CreateLoop(screen);
        StallContext? seen = null;
        Task<StallResolution> OnStall(StallContext context, CancellationToken _)
        {
            seen = context;
            screen.BlockNextClicks = 0;   // the user moved the covering window away
            return Task.FromResult(StallResolution.Retry);
        }

        var report = await loop.RunAsync(null, OnStall, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.NotNull(seen);
        Assert.Equal(StallCause.NextButtonUnreachable, seen!.Cause);
        Assert.Equal(NextClickOutcome.Covered, seen.ClickOutcome);
        Assert.Equal(3, seen.Attempts);
    }

    [Fact]
    public async Task Run_SenderWindowGone_RecalibratorRefindsIt_NoStall()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { GoneNextClicks = 2 };
        var recalibrator = new FakeRecalibrator();
        var loop = CreateLoop(screen, recalibrator);

        var report = await loop.RunAsync(null, Stop, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        // Observe-first inserts a read between attempts, so the exact count varies; what matters
        // is that recalibration ran and the user was never asked.
        Assert.True(recalibrator.NextButtonCalls >= 2);
        Assert.Equal(0, report.Stalls);
    }

    [Fact]
    public async Task Run_UnreadableWhileSenderAdvanced_AcceptsShownFrameInsteadOfSkipping()
    {
        // The reported field bug: the click landed but the frame was unreadable (partial cover).
        // After the user fixes it, the loop must READ the now-visible frame — which is the next
        // frame, already advanced — and accept it. A blind re-click here would skip it for good.
        var (frames, payload, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { DamageAfterClick = 1 };
        var loop = CreateLoop(screen, new FakeRecalibrator());
        Task<StallResolution> OnStall(StallContext context, CancellationToken _)
        {
            screen.DamageAfterClick = -1;   // the user uncovered the frame
            return Task.FromResult(StallResolution.Retry);
        }

        using var guard = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var report = await loop.RunAsync(null, OnStall, guard.Token);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.Equal(frames.Count - 1, report.FramesReceived);
        Assert.Equal(payload, report.Assembler!.AssembleAndVerify());
        // Exactly one click per payload frame: the frame shown after the fix was captured, not clicked past.
        Assert.Equal(frames.Count - 1, screen.ClickCount);
    }

    [Fact]
    public async Task Run_SenderWindowGone_NoRecalibrator_AsksImmediately()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { GoneNextClicks = 1 };
        var loop = CreateLoop(screen);
        StallContext? seen = null;
        Task<StallResolution> OnStall(StallContext context, CancellationToken _)
        {
            seen ??= context;
            return Task.FromResult(StallResolution.Retry);
        }

        var report = await loop.RunAsync(null, OnStall, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.NotNull(seen);
        Assert.Equal(StallCause.NextButtonUnreachable, seen!.Cause);
        Assert.Equal(NextClickOutcome.WindowGone, seen.ClickOutcome);
        Assert.Equal(1, seen.Attempts);
    }

    [Fact]
    public async Task Run_CleanMultiFrameTransfer_CompletesAndVerifies()
    {
        var (frames, payload, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames);
        var loop = CreateLoop(screen);

        var report = await loop.RunAsync(null, Stop, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.NotNull(report.Assembler);
        Assert.Equal(payload, report.Assembler!.AssembleAndVerify());
        Assert.Equal(frames.Count - 1, report.FramesReceived);
        Assert.Equal(0, report.Reclicks);
    }

    [Fact]
    public async Task Run_CleanTransfer_ReportsPassQualityPerAcceptedFrame()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames);
        var loop = CreateLoop(screen);
        var progress = new CollectingProgress();

        var report = await loop.RunAsync(progress, Stop, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        var quality = progress.Items.Where(s => s.Quality is not null).Select(s => s.Quality!).ToList();
        Assert.NotEmpty(quality);
        // Exact PNG round-trip → every accepted frame decodes cleanly.
        Assert.All(quality, q => Assert.Equal(FrameQualityVerdict.Pass, q.Verdict));
        Assert.All(quality, q => Assert.True(q.DataTiles > 0 && q.TimingMatchRatio > 0.9));
    }

    [Fact]
    public async Task Run_Frame0Read_LogsTheTransferDetails()
    {
        var (frames, _, metadata) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames);
        var loop = CreateLoop(screen);
        var progress = new CollectingProgress();

        var report = await loop.RunAsync(progress, Stop, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        var detail = progress.Items.FirstOrDefault(s => s.Message.StartsWith("Receiving"));
        Assert.NotNull(detail);
        Assert.Contains("loop.bin", detail!.Message);
        Assert.Contains("file", detail.Message);
        Assert.Contains($"{metadata.TotalFrames - 1} frames", detail.Message);
        Assert.Contains("Medium ECC", detail.Message);
        Assert.Contains("256 colours", detail.Message);
    }

    [Fact]
    public async Task Run_CleanTransfer_ReportsShownFrameIdOnAccepts()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames);
        var loop = CreateLoop(screen);
        var progress = new CollectingProgress();

        var report = await loop.RunAsync(progress, Stop, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        var shown = progress.Items.Where(s => s.ShownFrameId is not null).Select(s => s.ShownFrameId!.Value).ToList();
        Assert.NotEmpty(shown);
        Assert.Contains(1u, shown);
    }

    [Fact]
    public async Task Run_DroppedClicks_RecoversByReclicking()
    {
        var (frames, payload, _) = BuildTransfer(20_000);
        var screen = new FakeScreen(frames) { IgnoreNextClicks = 2 };
        var loop = CreateLoop(screen);

        var report = await loop.RunAsync(null, Stop, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.Equal(payload, report.Assembler!.AssembleAndVerify());
        Assert.True(report.Reclicks > 0, "Dropped clicks should have forced at least one re-click.");
    }

    [Fact]
    public async Task Run_DroppedClicks_RecalibratesButtonOnEachRetry()
    {
        var (frames, _, _) = BuildTransfer(20_000);
        var screen = new FakeScreen(frames) { IgnoreNextClicks = 2 };
        var recalibrator = new FakeRecalibrator();
        var loop = CreateLoop(screen, recalibrator);

        var report = await loop.RunAsync(null, Stop, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.Equal(2, recalibrator.NextButtonCalls);
    }

    [Fact]
    public async Task Run_ClickIneffective_ButtonNotFoundOnRetry_AsksImmediately()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { IgnoreNextClicks = 1000 };
        var recalibrator = new FakeRecalibrator { NextButtonFindable = false };
        var loop = CreateLoop(screen, recalibrator);
        StallContext? seen = null;
        Task<StallResolution> OnStall(StallContext context, CancellationToken _)
        {
            seen ??= context;
            return Task.FromResult(StallResolution.Stop);
        }

        var report = await loop.RunAsync(null, OnStall, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Stopped, report.State);
        Assert.NotNull(seen);
        Assert.Equal(StallCause.NextButtonUnreachable, seen!.Cause);
        Assert.Equal(1, recalibrator.NextButtonCalls);
    }

    [Fact]
    public async Task Run_ClientStuck_StallsAsClickIneffective_ThenStopsCleanly()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { IgnoreNextClicks = 1000 };
        var loop = CreateLoop(screen);
        StallContext? seen = null;
        Task<StallResolution> OnStall(StallContext context, CancellationToken _)
        {
            seen ??= context;
            return Task.FromResult(StallResolution.Stop);
        }

        var report = await loop.RunAsync(null, OnStall, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Stopped, report.State);
        Assert.True(report.Stalls >= 1);
        Assert.Equal(StallCause.NextClickIneffective, seen!.Cause);
        Assert.False(report.Assembler?.IsComplete ?? false);
    }

    [Fact]
    public async Task Run_UnreadableFrame_RetriesWithoutClicking_ThenStallsAsFrameUnreadable()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { DamageAfterClick = 1 };
        var recalibrator = new FakeRecalibrator();
        var loop = CreateLoop(screen, recalibrator);
        StallContext? seen = null;
        Task<StallResolution> OnStall(StallContext context, CancellationToken _)
        {
            seen ??= context;
            return Task.FromResult(StallResolution.Stop);
        }

        var report = await loop.RunAsync(null, OnStall, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Stopped, report.State);
        Assert.NotNull(seen);
        Assert.Equal(StallCause.FrameUnreadable, seen!.Cause);
        // The click that revealed the damage is the only one — never click what can't be read.
        Assert.Equal(1, screen.ClickCount);
        Assert.Equal(2, recalibrator.FrameCalls);
    }

    [Fact]
    public async Task Run_BlankScreen_RecalibrationFails_StallsAsFrameNotDetected()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { BlankAfterClick = 1 };
        var recalibrator = new FakeRecalibrator { FrameFindable = false };
        var loop = CreateLoop(screen, recalibrator);
        StallContext? seen = null;
        Task<StallResolution> OnStall(StallContext context, CancellationToken _)
        {
            seen ??= context;
            return Task.FromResult(StallResolution.Stop);
        }

        var report = await loop.RunAsync(null, OnStall, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Stopped, report.State);
        Assert.NotNull(seen);
        Assert.Equal(StallCause.FrameNotDetected, seen!.Cause);
        Assert.Equal(1, seen.Attempts);
        Assert.Equal(1, recalibrator.FrameCalls);
        Assert.Equal(1, screen.ClickCount);
    }

    [Fact]
    public async Task Run_Frame0NeverVisible_AsksWithFrameNotDetected_StopKeepsNothing()
    {
        var (frames, _, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { BlankAfterClick = 0 };   // blank before the first click
        var recalibrator = new FakeRecalibrator { FrameFindable = false };
        var loop = CreateLoop(screen, recalibrator);
        StallContext? seen = null;
        Task<StallResolution> OnStall(StallContext context, CancellationToken _)
        {
            seen ??= context;
            return Task.FromResult(StallResolution.Stop);
        }

        var report = await loop.RunAsync(null, OnStall, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Stopped, report.State);
        Assert.Equal(0, report.FramesReceived);
        Assert.NotNull(seen);
        Assert.Equal(StallCause.FrameNotDetected, seen!.Cause);
        Assert.True(recalibrator.FrameCalls >= 1, "Frame recalibration should run before asking the user.");
        Assert.Equal(0, screen.ClickCount);
    }

    [Fact]
    public async Task Run_Frame0NeverVisible_RetryAfterFixing_Completes()
    {
        var (frames, payload, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { BlankAfterClick = 0 };
        var loop = CreateLoop(screen, new FakeRecalibrator { FrameFindable = false });
        Task<StallResolution> OnStall(StallContext _, CancellationToken __)
        {
            screen.BlankAfterClick = -1;   // the user brought the sender back
            return Task.FromResult(StallResolution.Retry);
        }

        var report = await loop.RunAsync(null, OnStall, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        Assert.Equal(payload, report.Assembler!.AssembleAndVerify());
    }

    [Fact]
    public async Task Run_StallThenRetryResolution_Completes()
    {
        var (frames, payload, _) = BuildTransfer(25_000);
        var screen = new FakeScreen(frames) { IgnoreNextClicks = 5 };
        var loop = CreateLoop(screen);

        // First stall: stop ignoring clicks, then retry — the transfer should finish.
        Task<StallResolution> OnStall(StallContext _, CancellationToken __)
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
        var runTask = loop.RunAsync(null, Stop, cts.Token);

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

        var report = await loop.RunAsync(null, Stop, CancellationToken.None);

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
        var report = await loop.RunAsync(progress, Stop, CancellationToken.None);

        Assert.Equal(CaptureLoopState.Complete, report.State);
        var recovering = progress.Items.Where(s => s.State == CaptureLoopState.RecoveringGaps).ToList();
        Assert.NotEmpty(recovering);
        Assert.Contains(recovering, s => s.MissingFrameIds is { Count: > 0 } m && m.Contains(2u));
    }

    [Fact]
    public async Task Run_Cancellation_ReturnsStopped()
    {
        var (frames, _, _) = BuildTransfer(200_000);
        var screen = new FakeScreen(frames) { IgnoreNextClicks = 100000 };
        var loop = new CaptureLoopService(screen, screen, ColorMap.Default,
            new CaptureLoopOptions(PollIntervalMs: 10, StabilityIntervalMs: 5));
        using var cts = new CancellationTokenSource();

        var task = loop.RunAsync(null, Stop, cts.Token);
        cts.CancelAfter(50);
        var report = await task;

        Assert.Equal(CaptureLoopState.Stopped, report.State);
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
                new CaptureLoopOptions(PollIntervalMs: 0, StabilityIntervalMs: 0, MaxPollsPerClick: 6, MaxReclicks: 3, BlockedRetryIntervalMs: 0),
                assemblerFactory: m => service.OpenAssembler(root, m));

            Task<ResumeMode> OnResume(ResumeContext ctx, CancellationToken _)
            {
                Assert.Equal(prefix, ctx.ReceivedFrames);
                Assert.Equal((uint)(prefix + 1), ctx.FirstMissingFrameId);
                return Task.FromResult(ResumeMode.Automatic);
            }

            var report = await loop.RunAsync(null, Stop, CancellationToken.None, OnResume);

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
                new CaptureLoopOptions(PollIntervalMs: 0, StabilityIntervalMs: 0, MaxPollsPerClick: 6, MaxReclicks: 3, BlockedRetryIntervalMs: 0),
                assemblerFactory: m => service.OpenAssembler(root, m));

            // Manual: the user navigates the sender to the first missing frame, then continues.
            Task<ResumeMode> OnResume(ResumeContext ctx, CancellationToken _)
            {
                screen.JumpTo((int)ctx.FirstMissingFrameId);
                return Task.FromResult(ResumeMode.Manual);
            }

            var report = await loop.RunAsync(null, Stop, CancellationToken.None, OnResume);

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
                new CaptureLoopOptions(PollIntervalMs: 0, StabilityIntervalMs: 0, MaxPollsPerClick: 6, MaxReclicks: 3, BlockedRetryIntervalMs: 0),
                assemblerFactory: m => service.OpenAssembler(root, m));

            var report = await loop.RunAsync(null, Stop, CancellationToken.None,
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
