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
/// never reads a frame mid-repaint. Failures are diagnosed into distinct causes (button
/// unreachable, click ineffective, frame unreadable, frame not detected); each is retried with
/// automatic recalibration before the loop pauses and asks the user.
/// </summary>
public sealed class CaptureLoopService
{
    private readonly IScreenCapture _capture;
    private readonly INextClicker _clicker;
    private readonly FrameDecoder _decoder;
    private readonly CaptureLoopOptions _options;
    private readonly ILogger<CaptureLoopService>? _logger;
    private readonly Func<MetadataPayload, PayloadAssembler> _assemblerFactory;
    private readonly ILoopRecalibrator? _recalibrator;
    private readonly object _pauseLock = new();
    private TaskCompletionSource<bool>? _pauseGate;

    // Adopted from frame 0; only payload frames vary, frame 0 stays the fixed mono bootstrap.
    private FrameLayout _payloadLayout = FrameLayout.Default;
    private FrameDecoder _payloadDecoder;
    private int _payloadBits = 8;

    // Poll-tick memory: the frame id last read off the screen (for the same-frame decode
    // short-circuit) and the verdict of the last processed capture, keyed by its fingerprint,
    // so an unchanged screen is never decoded twice.
    private uint? _lastShownId;
    private (long Print, ObservedTick Tick)? _lastTick;

    /// <summary>Gets the payload-frame layout adopted from frame 0, so a caller can re-find the frame on screen.</summary>
    public FrameLayout PayloadLayout => _payloadLayout;

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
    /// The optional recalibrator lets the loop re-find the Next button and the frame region
    /// automatically between retries.
    /// </summary>
    public CaptureLoopService(
        IScreenCapture capture,
        INextClicker clicker,
        ColorMap colorMap,
        CaptureLoopOptions? options = null,
        ILogger<CaptureLoopService>? logger = null,
        Func<MetadataPayload, PayloadAssembler>? assemblerFactory = null,
        ILoopRecalibrator? recalibrator = null)
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
        _recalibrator = recalibrator;
    }

    /// <summary>
    /// Runs the loop until the transfer completes, fails, is stopped at a stall, or is cancelled.
    /// </summary>
    /// <param name="progress">Status sink.</param>
    /// <param name="onStall">Invoked when stalled; receives the diagnosed cause and returns how the user wants to resolve it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onResume">
    /// Invoked when an interrupted reception is recognized (the assembler already holds frames);
    /// returns how the user wants to resume. Null resumes automatically.
    /// </param>
    public async Task<TransferReport> RunAsync(
        IProgress<LoopStatus>? progress,
        Func<StallContext, CancellationToken, Task<StallResolution>>? onStall,
        CancellationToken cancellationToken,
        Func<ResumeContext, CancellationToken, Task<ResumeMode>>? onResume = null)
    {
        MetadataPayload? metadata = null;
        PayloadAssembler? assembler = null;
        uint lastFrameId = 0;
        var counters = new Counters();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _lastShownId = null;
        _lastTick = null;

        try
        {
            metadata = await AcquireFrame0Async(progress, onStall, counters, cancellationToken);
            if (metadata is null)
                return new TransferReport(CaptureLoopState.Stopped, null, null, 0, 0, 0, counters.Stalls, stopwatch.Elapsed, "Stopped while looking for the first frame.");

            assembler = _assemblerFactory(metadata);
            int total = (int)metadata.TotalFrames;

            if (assembler.ReceivedFrames > 0 &&
                !await PrepareResumeAsync(assembler, metadata, total, onResume, onStall, progress, counters, cancellationToken))
            {
                return new TransferReport(CaptureLoopState.Stopped, metadata, null, assembler.ReceivedFrames, total, counters.Reclicks, counters.Stalls, stopwatch.Elapsed, "Stopped during resume.");
            }

            lastFrameId = assembler.LastAcceptedId;
            Report(progress, CaptureLoopState.ClickingNext, assembler, metadata, lastFrameId, 0, DescribeTransfer(metadata));
            Report(progress, CaptureLoopState.ClickingNext, assembler, metadata, lastFrameId, 0,
                assembler.ReceivedFrames > 0
                    ? $"Resumed at {assembler.ReceivedFrames}/{assembler.ExpectedPayloadFrames} frames. Continuing transfer."
                    : "Frame 0 read. Starting transfer.");

            int reclicks = 0;        // click-ineffective tries on the current frame
            int channelRetries = 0;  // unreadable / not-detected tries

            // A click is only ever allowed after reading the frame and seeing one we already
            // have: an unconfirmed click may have advanced the sender invisibly, and clicking
            // blind on top of that skips a frame. Frame 0 was just read, so the first click is
            // justified; after any stall, recalibration, or error, observe first.
            bool shouldClick = true;

            while (!assembler.IsComplete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await WaitIfPausedAsync(cancellationToken);

                    // Highest frame seen but gaps remain — clicking Next can't reach them; recover.
                    if (assembler.LastAcceptedId >= assembler.LastPayloadFrameId)
                    {
                        await RecoverGapsAsync(assembler, metadata, progress, cancellationToken);
                        break;
                    }

                    if (shouldClick)
                    {
                        shouldClick = false;   // one click per observation that justified it
                        var attempt = await TryClickNextAsync(assembler, metadata, lastFrameId, onStall, progress, counters, cancellationToken);
                        if (attempt == ClickAttempt.Stopped)
                            return new TransferReport(CaptureLoopState.Stopped, metadata, null, assembler.ReceivedFrames, total, counters.Reclicks, counters.Stalls, stopwatch.Elapsed, "Stopped while the Next button was unreachable.");
                        if (attempt == ClickAttempt.Clicked)
                        {
                            // Empty message: this fires once per click and would otherwise flood the log; the
                            // live state label already shows "Waiting for the next frame…".
                            Report(progress, CaptureLoopState.WaitingForAdvance, assembler, metadata, lastFrameId, reclicks, "");
                        }

                        // ClickAttempt.Observe: a blocked-button stall was resolved — the user may
                        // have touched the sender, so fall through and read the frame first.
                    }

                    var round = await PollForAdvanceAsync(assembler, metadata, reclicks, progress, cancellationToken);
                    if (round.Advanced)
                    {
                        lastFrameId = assembler.LastAcceptedId;
                        reclicks = 0;
                        channelRetries = 0;
                        shouldClick = true;   // the sender is showing the frame just accepted
                        continue;
                    }

                    switch (round.Sight)
                    {
                        case PollSight.SameOrOldFrame:
                            // Read fine and parked on a frame we have — clicking is safe and needed.
                            reclicks++;
                            counters.Reclicks++;
                            if (reclicks >= _options.MaxReclicks)
                            {
                                var resolution = await RaiseStallAsync(onStall, progress, counters, assembler, metadata, lastFrameId, reclicks,
                                    new StallContext(StallCause.NextClickIneffective,
                                        $"Clicked Next {reclicks} times, but the sender is still showing frame {round.ShownFrameId ?? lastFrameId}.",
                                        reclicks),
                                    cancellationToken);
                                if (resolution == StallResolution.Stop)
                                    return StoppedAtStall(metadata, assembler, total, counters, stopwatch.Elapsed);
                                reclicks = 0;   // the user may have advanced the sender: observe, don't click
                            }
                            else if (_recalibrator is not null && !await _recalibrator.RecalibrateNextButtonAsync(cancellationToken))
                            {
                                // The button vanished mid-retry — ask right away rather than burning tries.
                                var resolution = await RaiseStallAsync(onStall, progress, counters, assembler, metadata, lastFrameId, reclicks,
                                    new StallContext(StallCause.NextButtonUnreachable,
                                        "The sender's Next button can't be found anymore.", reclicks),
                                    cancellationToken);
                                if (resolution == StallResolution.Stop)
                                    return StoppedAtStall(metadata, assembler, total, counters, stopwatch.Elapsed);
                                reclicks = 0;
                            }
                            else
                            {
                                shouldClick = true;
                            }

                            break;

                        case PollSight.Unreadable:
                            // Never click while the frame can't be read — that advances frames invisibly.
                            channelRetries++;
                            if (channelRetries >= _options.MaxReclicks)
                            {
                                var resolution = await RaiseStallAsync(onStall, progress, counters, assembler, metadata, lastFrameId, channelRetries,
                                    new StallContext(StallCause.FrameUnreadable,
                                        "The frame is visible but can't be read — it may be partially covered, or the channel is degraded.",
                                        channelRetries),
                                    cancellationToken);
                                if (resolution == StallResolution.Stop)
                                    return StoppedAtStall(metadata, assembler, total, counters, stopwatch.Elapsed);
                                channelRetries = 0;
                            }
                            else if (_recalibrator is not null)
                            {
                                await _recalibrator.RecalibrateFrameAsync(cancellationToken);
                            }

                            break;

                        case PollSight.NoFrame:
                            channelRetries++;
                            bool found = _recalibrator is not null && await _recalibrator.RecalibrateFrameAsync(cancellationToken);
                            if (!found || channelRetries >= _options.MaxReclicks)
                            {
                                var resolution = await RaiseStallAsync(onStall, progress, counters, assembler, metadata, lastFrameId, channelRetries,
                                    new StallContext(StallCause.FrameNotDetected,
                                        "No frame can be seen in the capture — the sender may be covered, moved, or no longer showing a frame.",
                                        channelRetries),
                                    cancellationToken);
                                if (resolution == StallResolution.Stop)
                                    return StoppedAtStall(metadata, assembler, total, counters, stopwatch.Elapsed);
                                channelRetries = 0;
                            }

                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A transient error (capture glitch, disk hiccup, one bad frame) is a stall, not the end.
                    _logger?.LogError(ex, "Transfer error; asking the user");
                    var resolution = await RaiseStallAsync(onStall, progress, counters, assembler, metadata, lastFrameId, reclicks,
                        new StallContext(StallCause.Error, $"Something went wrong: {ex.Message}", reclicks),
                        cancellationToken);
                    if (resolution == StallResolution.Stop)
                        return new TransferReport(CaptureLoopState.Stopped, metadata, null, assembler.ReceivedFrames, total, counters.Reclicks, counters.Stalls, stopwatch.Elapsed, ex.Message);

                    reclicks = 0;
                    channelRetries = 0;
                    shouldClick = false;   // observe before touching anything again
                }
            }

            Report(progress, CaptureLoopState.Reassembling, assembler, metadata, lastFrameId, 0, "Reassembling and verifying…");
            try
            {
                assembler.Verify();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string error = $"Verification failed: {ex.Message} The reception is kept — delete it from Received and run the transfer again.";
                Report(progress, CaptureLoopState.Failed, assembler, metadata, lastFrameId, 0, error);
                return new TransferReport(CaptureLoopState.Failed, metadata, null, assembler.ReceivedFrames, total, counters.Reclicks, counters.Stalls, stopwatch.Elapsed, error);
            }

            Report(progress, CaptureLoopState.Complete, assembler, metadata, lastFrameId, 0, "Transfer complete and verified.");

            return new TransferReport(CaptureLoopState.Complete, metadata, assembler, assembler.ReceivedFrames, total, counters.Reclicks, counters.Stalls, stopwatch.Elapsed, null);
        }
        catch (OperationCanceledException)
        {
            return Stopped(metadata, assembler, counters, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Capture loop failed");
            int total = metadata is null ? 0 : (int)metadata.TotalFrames;
            Report(progress, CaptureLoopState.Failed, assembler, metadata, lastFrameId, 0, ex.Message);
            return new TransferReport(CaptureLoopState.Failed, metadata, null, assembler?.ReceivedFrames ?? 0, total, counters.Reclicks, counters.Stalls, stopwatch.Elapsed, ex.Message);
        }
    }

    /// <summary>
    /// Hunts for frame 0 with the same policy as the payload loop: misses are diagnosed, the frame
    /// is auto-recalibrated halfway through the budget, and once it runs out the user gets the
    /// cause-specific prompt. The one frame-0-specific case is a payload frame on screen — only
    /// the user can navigate back, so it reports that and keeps waiting without counting a failure.
    /// </summary>
    /// <returns>The decoded metadata, or null if the user stopped.</returns>
    private async Task<MetadataPayload?> AcquireFrame0Async(
        IProgress<LoopStatus>? progress,
        Func<StallContext, CancellationToken, Task<StallResolution>>? onStall,
        Counters counters,
        CancellationToken cancellationToken)
    {
        const int WrongFrame = 4;
        int recalAt = Math.Max(1, _options.Frame0FailuresBeforeWarning / 2);
        int askAt = _options.Frame0FailuresBeforeWarning * 2;

        int failures = 0, noFrame = 0, unreadable = 0, errors = 0;
        int lastKind = 0;   // 0 none, 1 no frame, 2 unreadable, 3 error, 4 wrong frame shown
        string lastError = "", lastMessage = "";
        long? lastPrint = null;
        var attemptWatch = new System.Diagnostics.Stopwatch();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(cancellationToken);

            int kind;
            string message;
            try
            {
                attemptWatch.Restart();
                var (capture, print) = await CaptureStableAsync(cancellationToken);
                using var image = capture;

                if (lastPrint == print)
                {
                    // Unchanged screen: decoding again can't succeed, but the miss still counts.
                    kind = lastKind;
                    message = lastMessage;
                }
                else
                {
                    lastPrint = print;
                    long stableMs = attemptWatch.ElapsedMilliseconds;
                    var result = _decoder.DecodeMetadataFrame(image);
                    _logger?.LogDebug(
                        "Frame-0 attempt: stability {StableMs} ms, decode {DecodeMs} ms, {Status}/{Reason}",
                        stableMs, attemptWatch.ElapsedMilliseconds - stableMs, result.Status, result.FailureReason);

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
                                    : new FrameDecoder(ColorMap.FromCount(metadata.ColorCount, metadata.PaletteKind));
                                return metadata;
                            }
                        }
                        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
                        {
                            // decoded but not usable — falls through as unreadable
                        }
                    }

                    var probe = _decoder.TryProbe(image);
                    if (probe.Registered && probe.Header is { IsMetadataFrame: false, FrameId: var id })
                    {
                        kind = WrongFrame;
                        message = $"Showing frame {id} — go back to the first frame on the sender to start.";
                    }
                    else if (result.Status == DecodeStatus.Undecodable && result.FailureReason
                        is DecodeFailureReason.FiducialsNotFound or DecodeFailureReason.GeometryInvalid)
                    {
                        kind = 1;
                        message = "";   // quiet until the warning threshold
                    }
                    else
                    {
                        kind = 2;
                        message = "The first frame is visible but can't be read — it may be partially covered, or the channel is degraded.";
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                kind = 3;
                lastError = ex.Message;
                message = $"Capture isn't working: {ex.Message}";
            }

            if (kind == WrongFrame)
            {
                // The channel is healthy and the user is mid-navigation — not a failure.
                failures = 0; noFrame = 0; unreadable = 0; errors = 0;
            }
            else
            {
                failures++;
                if (kind == 1) noFrame++;
                else if (kind == 2) unreadable++;
                else if (kind == 3) errors++;

                if (failures == recalAt && _recalibrator is not null)
                    await _recalibrator.RecalibrateFrameAsync(cancellationToken);

                if (kind == 1 && failures >= _options.Frame0FailuresBeforeWarning)
                    message = "Can't see a Flux frame in the region — is the sender showing the first frame?";

                if (failures >= askAt)
                {
                    var cause = errors >= Math.Max(noFrame, unreadable) ? StallCause.Error
                        : unreadable > noFrame ? StallCause.FrameUnreadable
                        : StallCause.FrameNotDetected;
                    string stallMessage = cause switch
                    {
                        StallCause.Error => $"Capture isn't working: {lastError}",
                        StallCause.FrameUnreadable => "The first frame is visible but can't be read — it may be partially covered, or the channel is degraded.",
                        _ => "Can't see a Flux frame in the region — is the sender showing the first frame?",
                    };
                    var resolution = await RaiseStallAsync(onStall, progress, counters, null, null, 0, failures,
                        new StallContext(cause, stallMessage, failures), cancellationToken);
                    if (resolution == StallResolution.Stop)
                        return null;

                    failures = 0; noFrame = 0; unreadable = 0; errors = 0;
                    lastPrint = null;   // the user may have fixed things without changing the pixels we saw
                }
            }

            lastKind = kind;
            lastMessage = message;
            Report(progress, CaptureLoopState.WaitingForFrame0, null, null, 0, 0, message);
            await Task.Delay(_options.PollIntervalMs, cancellationToken);
        }
    }

    /// <summary>
    /// Recognizes an interrupted reception and, per the user's choice, seeks to and captures the
    /// first missing frame so the main loop can carry on. Already-received frames are not
    /// "acceptable" to the forward loop, so it would stall on them — this step skips past them.
    /// </summary>
    /// <returns>True to continue the transfer; false if the user stopped.</returns>
    private async Task<bool> PrepareResumeAsync(
        PayloadAssembler assembler,
        MetadataPayload metadata,
        int total,
        Func<ResumeContext, CancellationToken, Task<ResumeMode>>? onResume,
        Func<StallContext, CancellationToken, Task<StallResolution>>? onStall,
        IProgress<LoopStatus>? progress,
        Counters counters,
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
            onStall, progress, counters, cancellationToken);
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
        Func<StallContext, CancellationToken, Task<StallResolution>>? onStall,
        IProgress<LoopStatus>? progress,
        Counters counters,
        CancellationToken cancellationToken)
    {
        int reclicks = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(cancellationToken);

            var (capture, _) = await CaptureStableAsync(cancellationToken);
            using var image = capture;
            var probe = _payloadDecoder.TryProbe(image, _payloadLayout);
            uint? shown = probe.Registered && probe.Header is { } h ? h.FrameId : null;

            if (shown is { } id && id >= metadata.MetadataFrameCount && id < metadata.TotalFrames && !assembler.HasFrame(id))
            {
                var decoded = _payloadDecoder.Decode(image, bitsPerTile: _payloadBits, layout: _payloadLayout);
                if (decoded.Status == DecodeStatus.Success && decoded.Header is { } fullHeader &&
                    IsAcceptablePayloadFrame(fullHeader, metadata, assembler))
                {
                    assembler.AddFrame(fullHeader, decoded.Payload!);
                    Report(progress, CaptureLoopState.Resuming, assembler, metadata, fullHeader.FrameId, 0,
                        $"Resumed at frame {fullHeader.FrameId} ({assembler.ReceivedFrames}/{assembler.ExpectedPayloadFrames}).",
                        EncodeThumbnail(image), quality: QualityOf(decoded.Diagnostics), shownFrameId: fullHeader.FrameId);
                    return true;
                }
            }

            if (!allowClicking)
            {
                Report(progress, CaptureLoopState.Resuming, assembler, metadata, assembler.LastAcceptedId, 0,
                    $"Waiting for frame {firstMissing} — show it on the sender and it will be captured.", shownFrameId: shown);
                await Task.Delay(_options.PollIntervalMs, cancellationToken);
                continue;
            }

            var attempt = await TryClickNextAsync(assembler, metadata, assembler.LastAcceptedId, onStall, progress, counters, cancellationToken);
            if (attempt == ClickAttempt.Stopped)
                return false;
            if (attempt == ClickAttempt.Observe)
                continue;   // the seek loop re-probes the screen before every click anyway
            Report(progress, CaptureLoopState.Resuming, assembler, metadata, assembler.LastAcceptedId, reclicks,
                $"Skipping ahead to frame {firstMissing}…", shownFrameId: shown);

            if (await PollForProbeAdvanceAsync(shown, cancellationToken))
            {
                reclicks = 0;
                continue;
            }

            reclicks++;
            if (reclicks < _options.MaxReclicks)
                continue;

            var resolution = await RaiseStallAsync(onStall, progress, counters, assembler, metadata, assembler.LastAcceptedId, reclicks,
                new StallContext(StallCause.NextClickIneffective,
                    $"Skipping ahead isn't advancing — the sender is stuck before frame {firstMissing}.", reclicks),
                cancellationToken);
            if (resolution == StallResolution.Stop)
                return false;
            reclicks = 0;
        }
    }

    private enum ClickAttempt
    {
        /// <summary>The click was delivered to the sender.</summary>
        Clicked,

        /// <summary>The user chose Stop at the unreachable-button prompt.</summary>
        Stopped,

        /// <summary>The situation changed (stall resolved, window re-found) — read the frame
        /// before clicking anything: the sender may no longer be where it was.</summary>
        Observe,
    }

    /// <summary>
    /// Clicks Next, retrying a bounded number of times while the button is unreachable — a blocked
    /// click is never delivered blindly, and a vanished window triggers automatic recalibration.
    /// After the tries run out (immediately, when the button can't be re-found) the user is asked.
    /// Any recovery hands control back as <see cref="ClickAttempt.Observe"/> instead of clicking.
    /// </summary>
    private async Task<ClickAttempt> TryClickNextAsync(
        PayloadAssembler? assembler,
        MetadataPayload? metadata,
        uint lastFrameId,
        Func<StallContext, CancellationToken, Task<StallResolution>>? onStall,
        IProgress<LoopStatus>? progress,
        Counters counters,
        CancellationToken cancellationToken)
    {
        int attempts = 0;
        bool wasBlocked = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(cancellationToken);

            var outcome = _clicker.ClickNext();
            if (outcome == NextClickOutcome.Clicked)
            {
                if (wasBlocked)
                    Report(progress, CaptureLoopState.ClickingNext, assembler, metadata, lastFrameId, 0,
                        "The sender is reachable again — continuing.");
                return ClickAttempt.Clicked;
            }

            wasBlocked = true;
            attempts++;
            Report(progress, CaptureLoopState.ChannelBlocked, assembler, metadata, lastFrameId, attempts, BlockedMessage(outcome));

            // A gone window may have been reopened elsewhere; when the button is re-found, what
            // that window shows now is anyone's guess — observe, don't click.
            if (outcome == NextClickOutcome.WindowGone &&
                _recalibrator is not null && await _recalibrator.RecalibrateNextButtonAsync(cancellationToken))
            {
                return ClickAttempt.Observe;
            }

            // Covered/minimized may clear on their own within the retry budget; a window that is
            // gone and could not be re-found cannot, so ask at that try.
            if (outcome == NextClickOutcome.WindowGone || attempts >= _options.MaxReclicks)
            {
                var resolution = await RaiseStallAsync(onStall, progress, counters, assembler, metadata, lastFrameId, attempts,
                    new StallContext(StallCause.NextButtonUnreachable, BlockedMessage(outcome), attempts, outcome),
                    cancellationToken);
                if (resolution == StallResolution.Stop)
                    return ClickAttempt.Stopped;
                return ClickAttempt.Observe;
            }

            await Task.Delay(_options.BlockedRetryIntervalMs, cancellationToken);
        }
    }

    private static string BlockedMessage(NextClickOutcome outcome) => outcome switch
    {
        NextClickOutcome.Covered =>
            "Another window is covering the sender's Next button — bring the sender to the front (or move the viewer aside).",
        NextClickOutcome.Minimized => "The sender's window is minimized — restore it to continue.",
        _ => "The sender's window can't be found — reopen the cast on the sender.",
    };

    private async Task<StallResolution> RaiseStallAsync(
        Func<StallContext, CancellationToken, Task<StallResolution>>? onStall,
        IProgress<LoopStatus>? progress,
        Counters counters,
        PayloadAssembler? assembler,
        MetadataPayload? metadata,
        uint lastFrameId,
        int reclicks,
        StallContext context,
        CancellationToken cancellationToken)
    {
        counters.Stalls++;
        Report(progress, CaptureLoopState.Stalled, assembler, metadata, lastFrameId, reclicks, context.Message);
        return onStall is null ? StallResolution.Stop : await onStall(context, cancellationToken);
    }

    /// <summary>Polls until the displayed frame id differs from <paramref name="previousShown"/> (a click landed).</summary>
    private async Task<bool> PollForProbeAdvanceAsync(uint? previousShown, CancellationToken cancellationToken)
    {
        for (int poll = 0; poll < _options.MaxPollsPerClick; poll++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(cancellationToken);
            await Task.Delay(_options.PollIntervalMs, cancellationToken);

            var (capture, _) = await CaptureStableAsync(cancellationToken);
            using var image = capture;
            var probe = _payloadDecoder.TryProbe(image, _payloadLayout);
            if (probe.Registered && probe.Header is { } header &&
                (previousShown is null || header.FrameId != previousShown.Value))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<PollRound> PollForAdvanceAsync(
        PayloadAssembler assembler,
        MetadataPayload metadata,
        int reclicks,
        IProgress<LoopStatus>? progress,
        CancellationToken cancellationToken)
    {
        int readable = 0, unreadable = 0;
        uint? shown = null;

        for (int poll = 0; poll < _options.MaxPollsPerClick; poll++)
        {
            var tick = await ObserveTickAsync(assembler, metadata, cancellationToken);
            switch (tick.Kind)
            {
                case TickKind.Accepted:
                    Report(progress, CaptureLoopState.WaitingForAdvance, assembler, metadata, tick.Header.FrameId, reclicks,
                        $"Received frame {tick.Header.FrameId} ({assembler.ReceivedFrames}/{assembler.ExpectedPayloadFrames}).",
                        tick.Png, quality: tick.Quality, shownFrameId: tick.Header.FrameId);
                    return new PollRound(true, PollSight.SameOrOldFrame, tick.Header.FrameId);

                case TickKind.SameOrOldFrame:
                    readable++;
                    shown = tick.ShownFrameId ?? shown;
                    break;

                case TickKind.Unreadable:
                    unreadable++;
                    break;
            }
        }

        // Any readable sighting proves the channel works, so the click just didn't land; an
        // unreadable one proves at least the frame is there. Only a fully blind round is NoFrame.
        var sight = readable > 0 ? PollSight.SameOrOldFrame
            : unreadable > 0 ? PollSight.Unreadable
            : PollSight.NoFrame;
        return new PollRound(false, sight, shown);
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
            var tick = await ObserveTickAsync(assembler, metadata, cancellationToken);
            if (tick.Kind != TickKind.Accepted)
                continue;

            var stillMissing = assembler.MissingFrameIds;
            var message = stillMissing.Count == 0
                ? $"Recovered frame {tick.Header.FrameId}. All frames received."
                : $"Recovered frame {tick.Header.FrameId}. {FormatMissingMessage(stillMissing)}";
            Report(progress, CaptureLoopState.RecoveringGaps, assembler, metadata, tick.Header.FrameId, 0,
                message, tick.Png, stillMissing, quality: tick.Quality, shownFrameId: tick.Header.FrameId);
        }
    }

    /// <summary>
    /// One poll tick: capture a stable image and classify what is on screen, accepting a new
    /// payload frame when one is shown. An unchanged screen (same fingerprint) reuses the last
    /// verdict without decoding again.
    /// </summary>
    private async Task<ObservedTick> ObserveTickAsync(
        PayloadAssembler assembler,
        MetadataPayload metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitIfPausedAsync(cancellationToken);
        await Task.Delay(_options.PollIntervalMs, cancellationToken);

        var (capture, print) = await CaptureStableAsync(cancellationToken);
        using var image = capture;

        if (_lastTick is { } last && last.Print == print)
            return last.Tick;

        var decoded = _payloadDecoder.Decode(image, previousFrameId: _lastShownId, bitsPerTile: _payloadBits, layout: _payloadLayout);
        var tick = ClassifyTick(decoded, image, assembler, metadata);

        // An accepted frame must not be replayed from cache: once it is in the assembler, the same
        // screen is just an old frame.
        _lastTick = (print, tick.Kind == TickKind.Accepted
            ? new ObservedTick(TickKind.SameOrOldFrame, tick.Header, null, null, tick.Header.FrameId)
            : tick);
        return tick;
    }

    private ObservedTick ClassifyTick(
        FrameDecodeResult decoded, SKBitmap capture, PayloadAssembler assembler, MetadataPayload metadata)
    {
        switch (decoded.Status)
        {
            case DecodeStatus.Success when decoded.Header is { } header:
                _lastShownId = header.FrameId;
                if (IsAcceptablePayloadFrame(header, metadata, assembler))
                {
                    assembler.AddFrame(header, decoded.Payload!);
                    return new ObservedTick(TickKind.Accepted, header, EncodeThumbnail(capture), QualityOf(decoded.Diagnostics), header.FrameId);
                }

                return new ObservedTick(TickKind.SameOrOldFrame, header, null, null, header.FrameId);

            case DecodeStatus.SameFrameAsBefore when decoded.Header is { } header:
                _lastShownId = header.FrameId;
                return new ObservedTick(TickKind.SameOrOldFrame, header, null, null, header.FrameId);

            case DecodeStatus.Undecodable when decoded.FailureReason == DecodeFailureReason.GeometryInvalid
                && _decoder.TryProbe(capture, BootstrapFrame.Layout).Registered:
                // Fiducials line up with the bootstrap grid: the sender is still on frame 0 —
                // the click didn't take effect, the channel itself is fine.
                return new ObservedTick(TickKind.SameOrOldFrame, default, null, null, null);

            case DecodeStatus.Undecodable when decoded.FailureReason
                is DecodeFailureReason.FiducialsNotFound or DecodeFailureReason.GeometryInvalid:
                return new ObservedTick(TickKind.NoFrame, default, null, null, null);

            default:
                // Registered but not decodable: partially covered or a degraded channel.
                return new ObservedTick(TickKind.Unreadable, default, null, null, null);
        }
    }

    private FrameQuality QualityOf(DecodeDiagnostics diagnostics) => new(
        diagnostics.TimingMatchRatio,
        diagnostics.LowConfidenceDataTiles,
        _payloadLayout.DataTileCount,
        diagnostics.CorrectedErrors);

    /// <summary>Everything frame 0 tells us about the incoming transfer, as one log line.</summary>
    private static string DescribeTransfer(MetadataPayload metadata)
    {
        string kind = metadata.PayloadType == PayloadType.Raw ? "file" : "7z archive";
        string size = metadata.PayloadType == PayloadType.Raw || metadata.OriginalLength == metadata.PayloadLength
            ? FormatBytes(metadata.PayloadLength)
            : $"{FormatBytes(metadata.OriginalLength)} original → {FormatBytes(metadata.PayloadLength)} compressed";
        long payloadFrames = metadata.TotalFrames - metadata.MetadataFrameCount;
        return $"Receiving “{metadata.OriginalName}” — {kind}, {size}, {payloadFrames} frames, " +
               $"{metadata.EccLevel} ECC, {metadata.GridWidthTiles}×{metadata.GridHeightTiles} grid, {metadata.ColorCount} colours.";
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
        _ => $"{bytes} B",
    };

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
        header.FrameId >= metadata.MetadataFrameCount &&
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

    private async Task<(SKBitmap Bitmap, long Fingerprint)> CaptureStableAsync(CancellationToken cancellationToken)
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
                return (next, nextPrint);
            }

            previous.Dispose();
            previous = next;
            previousPrint = nextPrint;
        }

        return (previous, previousPrint);
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
        FrameQuality? quality = null,
        uint? shownFrameId = null)
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
            quality,
            shownFrameId));
    }

    private TransferReport StoppedAtStall(
        MetadataPayload metadata, PayloadAssembler assembler, int total, Counters counters, TimeSpan elapsed) =>
        new(CaptureLoopState.Stopped, metadata, null, assembler.ReceivedFrames, total, counters.Reclicks, counters.Stalls, elapsed, "Stopped at a stall.");

    private static TransferReport Stopped(MetadataPayload? metadata, PayloadAssembler? assembler, Counters counters, TimeSpan elapsed) =>
        new(CaptureLoopState.Stopped, metadata, null, assembler?.ReceivedFrames ?? 0,
            metadata is null ? 0 : (int)metadata.TotalFrames, counters.Reclicks, counters.Stalls, elapsed, null);

    private sealed class Counters
    {
        public int Reclicks;
        public int Stalls;
    }

    private enum PollSight
    {
        SameOrOldFrame,
        Unreadable,
        NoFrame,
    }

    private readonly record struct PollRound(bool Advanced, PollSight Sight, uint? ShownFrameId);

    private enum TickKind
    {
        Accepted,
        SameOrOldFrame,
        Unreadable,
        NoFrame,
    }

    private readonly record struct ObservedTick(
        TickKind Kind, FrameHeader Header, byte[]? Png, FrameQuality? Quality, uint? ShownFrameId);
}
