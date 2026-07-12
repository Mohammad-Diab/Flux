using FluxCore.Decoding;
using FluxCore.Framing;
using FluxCore.Imaging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace FluxCore.Transfer;

/// <summary>
/// Drives the Server's optical capture loop: watch the calibrated region, decode frames, click
/// the Client's Next button, and confirm advancement by the decoded frame id incrementing —
/// never by a timer. Requires two consecutive pixel-identical captures before decoding so it
/// never reads a frame mid-repaint. Stalls (no advance after repeated re-clicks) hand control
/// back to the user rather than spinning forever.
/// </summary>
public sealed class CaptureLoopService
{
    private readonly IScreenCapture _capture;
    private readonly INextClicker _clicker;
    private readonly FrameDecoder _decoder;
    private readonly CaptureLoopOptions _options;
    private readonly ILogger<CaptureLoopService>? _logger;
    private readonly Func<MetadataPayload, PayloadAssembler> _assemblerFactory;
    private readonly object _pauseLock = new();
    private TaskCompletionSource<bool>? _pauseGate;

    // Adopted from frame 0; only payload frames vary, frame 0 stays Default cube-corner.
    private FrameLayout _payloadLayout = FrameLayout.Default;
    private FrameDecoder _payloadDecoder;
    private int _payloadBits = 8;

    /// <summary>Gets a value indicating whether the loop is currently paused.</summary>
    public bool IsPaused
    {
        get { lock (_pauseLock) { return _pauseGate is not null; } }
    }

    /// <summary>Pauses the loop; it stops capturing and clicking until resumed. Idempotent.</summary>
    public void Pause()
    {
        lock (_pauseLock)
        {
            _pauseGate ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Resumes a paused loop. Idempotent.</summary>
    public void Resume()
    {
        lock (_pauseLock)
        {
            _pauseGate?.TrySetResult(true);
            _pauseGate = null;
        }
    }

    /// <summary>
    /// Creates the loop over a capture source, clicker, and decode palette. The optional
    /// assembler factory builds the payload assembler once frame 0 is read; supply one that
    /// returns a persisting assembler to enable resume (the default is a fresh in-memory one).
    /// </summary>
    public CaptureLoopService(
        IScreenCapture capture,
        INextClicker clicker,
        ColorMap colorMap,
        CaptureLoopOptions? options = null,
        ILogger<CaptureLoopService>? logger = null,
        Func<MetadataPayload, PayloadAssembler>? assemblerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(clicker);
        ArgumentNullException.ThrowIfNull(colorMap);

        _capture = capture;
        _clicker = clicker;
        _decoder = new FrameDecoder(colorMap);
        _payloadDecoder = _decoder;
        _options = options ?? new CaptureLoopOptions();
        _logger = logger;
        _assemblerFactory = assemblerFactory ?? (metadata => new PayloadAssembler(metadata));
    }

    /// <summary>
    /// Runs the loop until the transfer completes, fails, stalls to abort, or is cancelled.
    /// </summary>
    /// <param name="progress">Status sink.</param>
    /// <param name="onStall">Invoked when stalled; returns how the user wants to resolve it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onResume">
    /// Invoked when an interrupted reception is recognized (the assembler already holds frames);
    /// returns how the user wants to resume. Null resumes automatically.
    /// </param>
    public async Task<TransferReport> RunAsync(
        IProgress<LoopStatus>? progress,
        Func<CancellationToken, Task<StallResolution>>? onStall,
        CancellationToken cancellationToken,
        Func<ResumeContext, CancellationToken, Task<ResumeMode>>? onResume = null)
    {
        MetadataPayload? metadata = null;
        PayloadAssembler? assembler = null;
        uint lastFrameId = 0;
        int reclicks = 0;
        int totalReclicks = 0;
        int stalls = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            metadata = await AcquireFrame0Async(progress, cancellationToken);

            assembler = _assemblerFactory(metadata);
            int total = (int)metadata.TotalFrames;

            if (assembler.ReceivedFrames > 0 &&
                !await PrepareResumeAsync(assembler, metadata, total, onResume, onStall, progress, cancellationToken))
            {
                return new TransferReport(CaptureLoopState.Failed, metadata, null, assembler.ReceivedFrames, total, totalReclicks, stalls, stopwatch.Elapsed, "Aborted during resume.");
            }

            lastFrameId = assembler.LastAcceptedId;
            Report(progress, CaptureLoopState.ClickingNext, assembler, metadata, lastFrameId, 0,
                assembler.ReceivedFrames > 0
                    ? $"Resumed at {assembler.ReceivedFrames}/{assembler.ExpectedPayloadFrames} frames. Continuing transfer."
                    : "Frame 0 read. Starting transfer.");

            while (!assembler.IsComplete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(cancellationToken);

                // Highest frame seen but gaps remain — clicking Next can't reach them; recover.
                if (assembler.LastAcceptedId >= assembler.ExpectedPayloadFrames)
                {
                    await RecoverGapsAsync(assembler, metadata, progress, cancellationToken);
                    break;
                }

                _clicker.ClickNext();
                // Empty message: this fires once per click and would otherwise flood the log; the
                // live state label already shows "Waiting for the next frame…".
                Report(progress, CaptureLoopState.WaitingForAdvance, assembler, metadata, lastFrameId, reclicks, "");

                bool advanced = await PollForAdvanceAsync(assembler, metadata, reclicks, progress, cancellationToken);
                if (advanced)
                {
                    lastFrameId = assembler.LastAcceptedId;
                    reclicks = 0;
                    continue;
                }

                reclicks++;
                totalReclicks++;
                if (reclicks < _options.MaxReclicks)
                    continue;

                // Not advancing → let the user fix it and resume clicking; skipped frames are recovered at the end.
                stalls++;
                Report(progress, CaptureLoopState.Stalled, assembler, metadata, lastFrameId, reclicks,
                    $"Stuck at frame {assembler.LastAcceptedId + 1} after {reclicks} tries.");

                var resolution = onStall is null
                    ? StallResolution.Abort
                    : await onStall(cancellationToken);
                if (resolution == StallResolution.Abort)
                    return new TransferReport(CaptureLoopState.Failed, metadata, null, assembler.ReceivedFrames, total, totalReclicks, stalls, stopwatch.Elapsed, "Aborted at a stall.");

                reclicks = 0;
            }

            Report(progress, CaptureLoopState.Reassembling, assembler, metadata, lastFrameId, 0, "Reassembling and verifying…");
            assembler.Verify();
            Report(progress, CaptureLoopState.Complete, assembler, metadata, lastFrameId, 0, "Transfer complete and verified.");

            return new TransferReport(CaptureLoopState.Complete, metadata, assembler, assembler.ReceivedFrames, total, totalReclicks, stalls, stopwatch.Elapsed, null);
        }
        catch (OperationCanceledException)
        {
            return Cancelled(metadata, assembler, totalReclicks, stalls, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Capture loop failed");
            int total = metadata is null ? 0 : (int)metadata.TotalFrames;
            Report(progress, CaptureLoopState.Failed, assembler, metadata, lastFrameId, 0, ex.Message);
            return new TransferReport(CaptureLoopState.Failed, metadata, null, assembler?.ReceivedFrames ?? 0, total, totalReclicks, stalls, stopwatch.Elapsed, ex.Message);
        }
    }

    private async Task<MetadataPayload> AcquireFrame0Async(
        IProgress<LoopStatus>? progress, CancellationToken cancellationToken)
    {
        int failures = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var capture = await CaptureStableAsync(cancellationToken);
            var result = _decoder.DecodeMetadataFrame(capture);
            if (result.Status == DecodeStatus.Success)
            {
                try
                {
                    var metadata = MetadataPayload.Deserialize(result.Payload!);
                    if (metadata.TryBuildLayout(out var layout))
                    {
                        _payloadLayout = layout;
                        _payloadBits = metadata.BitsPerTile;
                        _payloadDecoder = metadata.ColorCount == 256
                            ? _decoder
                            : new FrameDecoder(ColorMap.FromCount(metadata.ColorCount));
                        return metadata;
                    }
                }
                catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
                {
                    // fall through to retry
                }
            }

            failures++;
            // A visible-but-wrong frame is actionable immediately; otherwise stay silent until the
            // warning threshold so the log isn't flooded before the transfer even starts.
            var probe = _decoder.TryProbe(capture);
            string message = probe.Registered && probe.Header is { IsMetadataFrame: false, FrameId: var id }
                ? $"Showing frame {id} — go back to the first frame on the sender to start."
                : failures >= _options.Frame0FailuresBeforeWarning
                    ? "Can't see a Flux frame in the region — is the sender showing the first frame?"
                    : "";
            Report(progress, CaptureLoopState.WaitingForFrame0, null, null, 0, 0, message);
            await Task.Delay(_options.PollIntervalMs, cancellationToken);
        }
    }

    /// <summary>
    /// Recognizes an interrupted reception and, per the user's choice, seeks to and captures the
    /// first missing frame so the main loop can carry on. Already-received frames are not
    /// "acceptable" to the forward loop, so it would stall on them — this step skips past them.
    /// </summary>
    /// <returns>True to continue the transfer; false if the user aborted.</returns>
    private async Task<bool> PrepareResumeAsync(
        PayloadAssembler assembler,
        MetadataPayload metadata,
        int total,
        Func<ResumeContext, CancellationToken, Task<ResumeMode>>? onResume,
        Func<CancellationToken, Task<StallResolution>>? onStall,
        IProgress<LoopStatus>? progress,
        CancellationToken cancellationToken)
    {
        var missing = assembler.MissingFrameIds;
        if (missing.Count == 0)
            return true;

        uint firstMissing = missing[0];
        Report(progress, CaptureLoopState.Resuming, assembler, metadata, assembler.LastAcceptedId, 0,
            $"Resuming — {assembler.ReceivedFrames}/{assembler.ExpectedPayloadFrames} frames already received.");

        var mode = onResume is null
            ? ResumeMode.Automatic
            : await onResume(new ResumeContext(assembler.ReceivedFrames, total, firstMissing), cancellationToken);

        if (mode == ResumeMode.StartOver)
        {
            assembler.Reset();
            return true;
        }

        return await SeekToMissingAsync(
            assembler, metadata, firstMissing, allowClicking: mode == ResumeMode.Automatic,
            onStall, progress, cancellationToken);
    }

    /// <summary>
    /// Advances to and captures the first missing frame. Automatic mode clicks Next to reach it;
    /// manual mode never clicks and captures whichever missing frame the user shows.
    /// </summary>
    private async Task<bool> SeekToMissingAsync(
        PayloadAssembler assembler,
        MetadataPayload metadata,
        uint firstMissing,
        bool allowClicking,
        Func<CancellationToken, Task<StallResolution>>? onStall,
        IProgress<LoopStatus>? progress,
        CancellationToken cancellationToken)
    {
        int reclicks = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(cancellationToken);

            using var capture = await CaptureStableAsync(cancellationToken);
            var probe = _payloadDecoder.TryProbe(capture, _payloadLayout);
            uint? shown = probe.Registered && probe.Header is { } h ? h.FrameId : null;

            if (shown is { } id && id >= 1 && id < metadata.TotalFrames && !assembler.HasFrame(id))
            {
                var decoded = _payloadDecoder.Decode(capture, bitsPerTile: _payloadBits, layout: _payloadLayout);
                if (decoded.Status == DecodeStatus.Success && decoded.Header is { } fullHeader &&
                    IsAcceptablePayloadFrame(fullHeader, metadata, assembler))
                {
                    assembler.AddFrame(fullHeader, decoded.Payload!);
                    Report(progress, CaptureLoopState.Resuming, assembler, metadata, fullHeader.FrameId, 0,
                        $"Resumed at frame {fullHeader.FrameId} ({assembler.ReceivedFrames}/{assembler.ExpectedPayloadFrames}).",
                        EncodeThumbnail(capture), quality: QualityOf(decoded.Diagnostics));
                    return true;
                }
            }

            if (!allowClicking)
            {
                Report(progress, CaptureLoopState.Resuming, assembler, metadata, assembler.LastAcceptedId, 0,
                    $"Waiting for frame {firstMissing} — show it on the sender and it will be captured.");
                await Task.Delay(_options.PollIntervalMs, cancellationToken);
                continue;
            }

            _clicker.ClickNext();
            Report(progress, CaptureLoopState.Resuming, assembler, metadata, assembler.LastAcceptedId, reclicks,
                $"Skipping ahead to frame {firstMissing}…");

            if (await PollForProbeAdvanceAsync(shown, cancellationToken))
            {
                reclicks = 0;
                continue;
            }

            reclicks++;
            if (reclicks < _options.MaxReclicks)
                continue;

            var resolution = onStall is null ? StallResolution.Abort : await onStall(cancellationToken);
            if (resolution == StallResolution.Abort)
                return false;
            reclicks = 0;
        }
    }

    /// <summary>Polls until the displayed frame id differs from <paramref name="previousShown"/> (a click landed).</summary>
    private async Task<bool> PollForProbeAdvanceAsync(uint? previousShown, CancellationToken cancellationToken)
    {
        for (int poll = 0; poll < _options.MaxPollsPerClick; poll++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(cancellationToken);
            await Task.Delay(_options.PollIntervalMs, cancellationToken);

            using var capture = await CaptureStableAsync(cancellationToken);
            var probe = _payloadDecoder.TryProbe(capture, _payloadLayout);
            if (probe.Registered && probe.Header is { } header &&
                (previousShown is null || header.FrameId != previousShown.Value))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> PollForAdvanceAsync(
        PayloadAssembler assembler,
        MetadataPayload metadata,
        int reclicks,
        IProgress<LoopStatus>? progress,
        CancellationToken cancellationToken)
    {
        for (int poll = 0; poll < _options.MaxPollsPerClick; poll++)
        {
            if (await TryAcquireAcceptableFrameAsync(assembler, metadata, cancellationToken) is not { } accepted)
                continue;

            Report(progress, CaptureLoopState.WaitingForAdvance, assembler, metadata, accepted.Header.FrameId, reclicks,
                $"Received frame {accepted.Header.FrameId} ({assembler.ReceivedFrames}/{assembler.ExpectedPayloadFrames}).",
                accepted.Png, quality: accepted.Quality);
            return true;
        }

        return false;
    }

    /// <summary>Waits (no clicking) for the user to re-show each skipped frame, capturing each until complete.</summary>
    private async Task RecoverGapsAsync(
        PayloadAssembler assembler,
        MetadataPayload metadata,
        IProgress<LoopStatus>? progress,
        CancellationToken cancellationToken)
    {
        var missing = assembler.MissingFrameIds;
        Report(progress, CaptureLoopState.RecoveringGaps, assembler, metadata, assembler.LastAcceptedId, 0,
            FormatMissingMessage(missing), null, missing);

        while (!assembler.IsComplete)
        {
            if (await TryAcquireAcceptableFrameAsync(assembler, metadata, cancellationToken) is not { } accepted)
                continue;

            var stillMissing = assembler.MissingFrameIds;
            var message = stillMissing.Count == 0
                ? $"Recovered frame {accepted.Header.FrameId}. All frames received."
                : $"Recovered frame {accepted.Header.FrameId}. {FormatMissingMessage(stillMissing)}";
            Report(progress, CaptureLoopState.RecoveringGaps, assembler, metadata, accepted.Header.FrameId, 0,
                message, accepted.Png, stillMissing, quality: accepted.Quality);
        }
    }

    /// <summary>One poll tick: capture a stable image, probe, fully decode, and accept a new payload frame.</summary>
    private async Task<(FrameHeader Header, byte[]? Png, FrameQuality Quality)?> TryAcquireAcceptableFrameAsync(
        PayloadAssembler assembler,
        MetadataPayload metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitIfPausedAsync(cancellationToken);
        await Task.Delay(_options.PollIntervalMs, cancellationToken);

        using var capture = await CaptureStableAsync(cancellationToken);

        var probe = _payloadDecoder.TryProbe(capture, _payloadLayout);
        if (!probe.Registered || probe.Header is not { } header)
            return null;
        if (!IsAcceptablePayloadFrame(header, metadata, assembler))
            return null;

        var decoded = _payloadDecoder.Decode(capture, bitsPerTile: _payloadBits, layout: _payloadLayout);
        if (decoded.Status != DecodeStatus.Success || decoded.Header is not { } fullHeader)
            return null;
        if (!IsAcceptablePayloadFrame(fullHeader, metadata, assembler))
            return null;

        assembler.AddFrame(fullHeader, decoded.Payload!);
        return (fullHeader, EncodeThumbnail(capture), QualityOf(decoded.Diagnostics));
    }

    private FrameQuality QualityOf(DecodeDiagnostics diagnostics) => new(
        diagnostics.TimingMatchRatio,
        diagnostics.LowConfidenceDataTiles,
        _payloadLayout.DataTileCount,
        diagnostics.CorrectedErrors);

    private static string FormatMissingMessage(IReadOnlyList<uint> missing)
    {
        if (missing.Count == 0)
            return "All frames received.";

        const int max = 12;
        string shown = string.Join(", ", missing.Take(max));
        string suffix = missing.Count > max ? $" … (+{missing.Count - max} more)" : "";
        return $"Missing {missing.Count} frame(s) — on the sender, use Back or “go to frame” to show: {shown}{suffix}";
    }

    private static bool IsAcceptablePayloadFrame(in FrameHeader header, MetadataPayload metadata, PayloadAssembler assembler) =>
        !header.IsMetadataFrame &&
        header.FrameId >= 1 &&
        header.FrameId < metadata.TotalFrames &&
        header.TotalFrames == metadata.TotalFrames &&
        !assembler.HasFrame(header.FrameId);

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task<bool>? gate;
        lock (_pauseLock)
        {
            gate = _pauseGate?.Task;
        }

        if (gate is not null)
            await gate.WaitAsync(cancellationToken);
    }

    private async Task<SKBitmap> CaptureStableAsync(CancellationToken cancellationToken)
    {
        var previous = _capture.Capture();
        long previousPrint = Fingerprint(previous);

        for (int attempt = 0; attempt < _options.StabilityMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(_options.StabilityIntervalMs, cancellationToken);

            var next = _capture.Capture();
            long nextPrint = Fingerprint(next);
            if (nextPrint == previousPrint)
            {
                previous.Dispose();
                return next;
            }

            previous.Dispose();
            previous = next;
            previousPrint = nextPrint;
        }

        return previous;
    }

    private static long Fingerprint(SKBitmap bitmap)
    {
        var span = bitmap.GetPixelSpan();
        const ulong offset = 1469598103934665603;
        const ulong prime = 1099511628211;
        ulong hash = offset;
        hash = (hash ^ (ulong)bitmap.Width) * prime;
        hash = (hash ^ (ulong)bitmap.Height) * prime;

        // Sample every 64th byte: fast, deterministic, and sensitive enough to detect a repaint.
        for (int i = 0; i < span.Length; i += 64)
        {
            hash = (hash ^ span[i]) * prime;
        }

        return (long)hash;
    }

    private static byte[]? EncodeThumbnail(SKBitmap bitmap)
    {
        try
        {
            int width = 240;
            int height = Math.Max(1, bitmap.Height * width / Math.Max(1, bitmap.Width));
            using var scaled = bitmap.Resize(new SKImageInfo(width, height), SKFilterQuality.Low);
            if (scaled is null)
                return null;
            using var image = SKImage.FromBitmap(scaled);
            using var data = image.Encode(SKEncodedImageFormat.Png, 80);
            return data.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static void Report(
        IProgress<LoopStatus>? progress,
        CaptureLoopState state,
        PayloadAssembler? assembler = null,
        MetadataPayload? metadata = null,
        uint lastFrameId = 0,
        int reclicks = 0,
        string message = "",
        byte[]? png = null,
        IReadOnlyList<uint>? missing = null,
        FrameQuality? quality = null)
    {
        progress?.Report(new LoopStatus(
            state,
            assembler?.ReceivedFrames ?? 0,
            metadata is null ? 0 : (int)metadata.TotalFrames,
            lastFrameId,
            reclicks,
            message,
            png,
            missing,
            assembler?.ReceivedBytes ?? 0,
            assembler is null ? 0 : (int)assembler.LastAcceptedId - assembler.ReceivedFrames,
            metadata?.PayloadLength ?? 0,
            quality));
    }

    private static TransferReport Cancelled(MetadataPayload? metadata, PayloadAssembler? assembler, int reclicks, int stalls, TimeSpan elapsed) =>
        new(CaptureLoopState.Cancelled, metadata, null, assembler?.ReceivedFrames ?? 0,
            metadata is null ? 0 : (int)metadata.TotalFrames, reclicks, stalls, elapsed, null);
}
