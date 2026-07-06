using FluxCore.Decoding;
using FluxCore.Framing;

namespace FluxCore.Transfer;

/// <summary>State of the optical capture loop.</summary>
public enum CaptureLoopState
{
    /// <summary>Looking for a decodable metadata frame (frame 0).</summary>
    WaitingForFrame0,

    /// <summary>Clicking the Client's Next button.</summary>
    ClickingNext,

    /// <summary>Polling for the frame id to advance after a click.</summary>
    WaitingForAdvance,

    /// <summary>Advance did not happen after repeated re-clicks; awaiting user intervention.</summary>
    Stalled,

    /// <summary>Skipped frames can't be reached by clicking forward; waiting for the user to re-show each.</summary>
    RecoveringGaps,

    /// <summary>All frames received; reassembling and verifying the payload.</summary>
    Reassembling,

    /// <summary>Transfer complete and verified.</summary>
    Complete,

    /// <summary>Transfer failed (e.g. verification error or user abort).</summary>
    Failed,

    /// <summary>Loop cancelled by the user.</summary>
    Cancelled,
}

/// <summary>How the user resolves a stall.</summary>
public enum StallResolution
{
    /// <summary>Try clicking again from the current frame.</summary>
    Retry,

    /// <summary>The Next-button point was recalibrated; resume clicking.</summary>
    Recalibrate,

    /// <summary>Abandon the transfer.</summary>
    Abort,
}

/// <summary>Tuning parameters for the capture loop.</summary>
/// <param name="PollIntervalMs">Delay between advance-poll captures.</param>
/// <param name="MaxPollsPerClick">Polls to wait for advance before re-clicking (K).</param>
/// <param name="MaxReclicks">Re-clicks before declaring a stall (R).</param>
/// <param name="StabilityMaxAttempts">Captures to try for two-identical stability before giving up.</param>
/// <param name="StabilityIntervalMs">Delay between stability captures.</param>
/// <param name="Frame0FailuresBeforeWarning">Consecutive frame-0 misses before surfacing a warning.</param>
public sealed record CaptureLoopOptions(
    int PollIntervalMs = 250,
    int MaxPollsPerClick = 8,
    int MaxReclicks = 3,
    int StabilityMaxAttempts = 12,
    int StabilityIntervalMs = 120,
    int Frame0FailuresBeforeWarning = 10);

/// <summary>Progress snapshot pushed to the UI as the loop runs.</summary>
/// <param name="State">Current loop state.</param>
/// <param name="ReceivedFrames">Distinct payload frames accepted so far.</param>
/// <param name="TotalFrames">Total frames including frame 0 (0 until metadata is read).</param>
/// <param name="LastFrameId">Frame id of the most recently accepted frame.</param>
/// <param name="Reclicks">Re-clicks spent on the current frame.</param>
/// <param name="Message">Human-readable status or warning.</param>
/// <param name="LastFramePng">PNG of the most recently accepted capture, for a thumbnail (optional).</param>
/// <param name="MissingFrameIds">Frame ids still missing (set only while recovering gaps).</param>
public sealed record LoopStatus(
    CaptureLoopState State,
    int ReceivedFrames,
    int TotalFrames,
    uint LastFrameId,
    int Reclicks,
    string Message,
    byte[]? LastFramePng = null,
    IReadOnlyList<uint>? MissingFrameIds = null);

/// <summary>Outcome of an optical transfer.</summary>
/// <param name="State">Terminal loop state.</param>
/// <param name="Metadata">Decoded transfer metadata, if frame 0 was read.</param>
/// <param name="Assembler">The completed payload assembler, on success.</param>
/// <param name="FramesReceived">Distinct payload frames received.</param>
/// <param name="TotalFrames">Total frames including frame 0.</param>
/// <param name="Reclicks">Total re-clicks across the transfer.</param>
/// <param name="Stalls">Number of stalls encountered.</param>
/// <param name="Elapsed">Wall-clock duration of the loop.</param>
/// <param name="Error">Failure detail, or null on success.</param>
public sealed record TransferReport(
    CaptureLoopState State,
    MetadataPayload? Metadata,
    PayloadAssembler? Assembler,
    int FramesReceived,
    int TotalFrames,
    int Reclicks,
    int Stalls,
    TimeSpan Elapsed,
    string? Error)
{
    /// <summary>Gets the average throughput in bytes per second (0 if not applicable).</summary>
    public double BytesPerSecond =>
        Metadata is null || Elapsed.TotalSeconds <= 0 ? 0 : Metadata.PayloadLength / Elapsed.TotalSeconds;

    /// <summary>Gets a one-line human summary of the transfer.</summary>
    public string Summary()
    {
        if (State != CaptureLoopState.Complete)
            return $"{State}: {FramesReceived}/{Math.Max(0, TotalFrames - 1)} frames in {Elapsed:mm\\:ss}" +
                   (Error is null ? "" : $" — {Error}");

        double kbps = BytesPerSecond / 1024;
        return $"Complete: {FramesReceived} frames in {Elapsed:mm\\:ss}, {Reclicks} re-clicks, {Stalls} stalls, {kbps:F1} KB/s.";
    }
}
