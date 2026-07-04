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

    /// <summary>
    /// Initializes a new instance of the <see cref="CaptureLoopService"/> class.
    /// </summary>
    /// <param name="capture">Region capture source.</param>
    /// <param name="clicker">Next-button clicker.</param>
    /// <param name="colorMap">Palette for decoding.</param>
    /// <param name="options">Loop tuning; defaults if null.</param>
    /// <param name="logger">Optional logger.</param>
    public CaptureLoopService(
        IScreenCapture capture,
        INextClicker clicker,
        ColorMap colorMap,
        CaptureLoopOptions? options = null,
        ILogger<CaptureLoopService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(clicker);
        ArgumentNullException.ThrowIfNull(colorMap);

        _capture = capture;
        _clicker = clicker;
        _decoder = new FrameDecoder(colorMap);
        _options = options ?? new CaptureLoopOptions();
        _logger = logger;
    }

    /// <summary>
    /// Runs the loop until the transfer completes, fails, stalls to abort, or is cancelled.
    /// </summary>
    /// <param name="progress">Status sink.</param>
    /// <param name="onStall">Invoked when stalled; returns how the user wants to resolve it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TransferReport> RunAsync(
        IProgress<LoopStatus>? progress,
        Func<CancellationToken, Task<StallResolution>>? onStall,
        CancellationToken cancellationToken)
    {
        MetadataPayload? metadata = null;
        PayloadAssembler? assembler = null;
        uint lastFrameId = 0;
        int reclicks = 0;
        int totalReclicks = 0;
        int stalls = 0;

        try
        {
            metadata = await AcquireFrame0Async(progress, cancellationToken);

            assembler = new PayloadAssembler(metadata);
            int total = (int)metadata.TotalFrames;
            Report(progress, CaptureLoopState.ClickingNext, assembler, metadata, lastFrameId, 0, "Frame 0 read. Starting transfer.");

            while (!assembler.IsComplete)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _clicker.ClickNext();
                Report(progress, CaptureLoopState.WaitingForAdvance, assembler, metadata, lastFrameId, reclicks, "Waiting for the next frame…");

                bool advanced = await PollForAdvanceAsync(assembler, metadata, lastFrameId, reclicks, progress, cancellationToken);
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

                stalls++;
                Report(progress, CaptureLoopState.Stalled, assembler, metadata, lastFrameId, reclicks,
                    $"Stuck at frame {lastFrameId} after {reclicks} clicks.");

                var resolution = onStall is null
                    ? StallResolution.Abort
                    : await onStall(cancellationToken);
                if (resolution == StallResolution.Abort)
                    return new TransferReport(CaptureLoopState.Failed, metadata, null, assembler.ReceivedFrames, total, totalReclicks, stalls, "Aborted at a stall.");

                reclicks = 0;
            }

            Report(progress, CaptureLoopState.Reassembling, assembler, metadata, lastFrameId, 0, "Reassembling and verifying…");
            assembler.AssembleAndVerify();
            Report(progress, CaptureLoopState.Complete, assembler, metadata, lastFrameId, 0, "Transfer complete and verified.");

            return new TransferReport(CaptureLoopState.Complete, metadata, assembler, assembler.ReceivedFrames, total, totalReclicks, stalls, null);
        }
        catch (OperationCanceledException)
        {
            return Cancelled(metadata, assembler, totalReclicks, stalls);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Capture loop failed");
            int total = metadata is null ? 0 : (int)metadata.TotalFrames;
            Report(progress, CaptureLoopState.Failed, assembler, metadata, lastFrameId, 0, ex.Message);
            return new TransferReport(CaptureLoopState.Failed, metadata, null, assembler?.ReceivedFrames ?? 0, total, totalReclicks, stalls, ex.Message);
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
                    if (metadata.MatchesFrameFormat())
                        return metadata;
                }
                catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
                {
                    // fall through to retry
                }
            }

            failures++;
            var message = failures >= _options.Frame0FailuresBeforeWarning
                ? "Can't see a Flux frame 0 in the region — is the Client showing the first frame?"
                : "Looking for frame 0…";
            Report(progress, CaptureLoopState.WaitingForFrame0, null, null, 0, 0, message);
            await Task.Delay(_options.PollIntervalMs, cancellationToken);
        }
    }

    private async Task<bool> PollForAdvanceAsync(
        PayloadAssembler assembler,
        MetadataPayload metadata,
        uint lastFrameId,
        int reclicks,
        IProgress<LoopStatus>? progress,
        CancellationToken cancellationToken)
    {
        for (int poll = 0; poll < _options.MaxPollsPerClick; poll++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(_options.PollIntervalMs, cancellationToken);

            using var capture = await CaptureStableAsync(cancellationToken);

            var probe = _decoder.TryProbe(capture);
            if (!probe.Registered || probe.Header is not { } header)
                continue;

            if (!IsAcceptablePayloadFrame(header, metadata, assembler))
                continue;

            var decoded = _decoder.Decode(capture);
            if (decoded.Status != DecodeStatus.Success || decoded.Header is not { } fullHeader)
                continue;

            if (!IsAcceptablePayloadFrame(fullHeader, metadata, assembler))
                continue;

            assembler.AddFrame(fullHeader, decoded.Payload!);
            var png = EncodeThumbnail(capture);
            Report(progress, CaptureLoopState.WaitingForAdvance, assembler, metadata, fullHeader.FrameId, reclicks,
                $"Received frame {fullHeader.FrameId} ({assembler.ReceivedFrames}/{assembler.ExpectedPayloadFrames}).", png);
            return true;
        }

        return false;
    }

    private static bool IsAcceptablePayloadFrame(in FrameHeader header, MetadataPayload metadata, PayloadAssembler assembler) =>
        !header.IsMetadataFrame &&
        header.FrameId >= 1 &&
        header.FrameId < metadata.TotalFrames &&
        header.TotalFrames == metadata.TotalFrames &&
        !assembler.HasFrame(header.FrameId);

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
        byte[]? png = null)
    {
        progress?.Report(new LoopStatus(
            state,
            assembler?.ReceivedFrames ?? 0,
            metadata is null ? 0 : (int)metadata.TotalFrames,
            lastFrameId,
            reclicks,
            message,
            png));
    }

    private static TransferReport Cancelled(MetadataPayload? metadata, PayloadAssembler? assembler, int reclicks, int stalls) =>
        new(CaptureLoopState.Cancelled, metadata, null, assembler?.ReceivedFrames ?? 0,
            metadata is null ? 0 : (int)metadata.TotalFrames, reclicks, stalls, null);
}
