using FluxCore.Framing;

namespace FluxCore.Decoding;

/// <summary>
/// Outcome category of a frame decode attempt.
/// </summary>
public enum DecodeStatus
{
    /// <summary>The frame decoded fully: header recovered, ECC succeeded, CRC verified.</summary>
    Success,

    /// <summary>The frame decoded to a different frame id than expected.</summary>
    WrongFrame,

    /// <summary>The frame decoded to the same frame id as the previous decode (Next has not taken effect yet).</summary>
    SameFrameAsBefore,

    /// <summary>The capture could not be decoded; see <see cref="FrameDecodeResult.FailureReason"/>.</summary>
    Undecodable,
}

/// <summary>
/// Reason a capture could not be decoded.
/// </summary>
public enum DecodeFailureReason
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>Fewer than four finder patterns were located.</summary>
    FiducialsNotFound,

    /// <summary>No corner assignment produced a homography whose timing pattern verified.</summary>
    GeometryInvalid,

    /// <summary>Too many low-confidence tiles; the capture has likely not converged to lossless yet.</summary>
    CaptureUnstable,

    /// <summary>No header copy could be recovered and validated.</summary>
    HeaderUnreadable,

    /// <summary>One or more payload codewords were damaged beyond Reed-Solomon repair.</summary>
    EccFailure,

    /// <summary>ECC succeeded but the payload CRC-32 did not match the header.</summary>
    CrcMismatch,
}

/// <summary>
/// Measurements collected during a decode attempt, surfaced for logging and stall diagnosis.
/// </summary>
public sealed class DecodeDiagnostics
{
    /// <summary>Gets the detected finder centers, if any.</summary>
    public FinderPoint[] FinderPoints { get; init; } = [];

    /// <summary>Gets the fraction of timing tiles matching the expected alternation (0-1).</summary>
    public double TimingMatchRatio { get; init; }

    /// <summary>Gets the number of data tiles classified with low confidence.</summary>
    public int LowConfidenceDataTiles { get; init; }

    /// <summary>Gets the mean palette distance across data tiles.</summary>
    public double MeanPaletteDistance { get; init; }

    /// <summary>Gets the worst palette distance across data tiles.</summary>
    public double MaxPaletteDistance { get; init; }

    /// <summary>Gets the number of Reed-Solomon symbol errors corrected across all codewords.</summary>
    public int CorrectedErrors { get; init; }

    /// <summary>Gets how many of the three header copies decoded to the accepted header.</summary>
    public int HeaderCopiesAgreeing { get; init; }
}

/// <summary>
/// Result of decoding one captured frame.
/// </summary>
public sealed class FrameDecodeResult
{
    /// <summary>Gets the outcome category.</summary>
    public required DecodeStatus Status { get; init; }

    /// <summary>Gets the failure reason when <see cref="Status"/> is Undecodable.</summary>
    public DecodeFailureReason FailureReason { get; init; }

    /// <summary>Gets the recovered frame header, when the header stage succeeded.</summary>
    public FrameHeader? Header { get; init; }

    /// <summary>Gets the recovered frame payload (exactly PayloadLength bytes), on success.</summary>
    public byte[]? Payload { get; init; }

    /// <summary>Gets the decode measurements.</summary>
    public required DecodeDiagnostics Diagnostics { get; init; }
}

/// <summary>
/// Result of a cheap probe: registration, beacon parity, and header only — no payload work.
/// Used by the capture loop to poll for frame advancement after clicking Next.
/// </summary>
public sealed class ProbeResult
{
    /// <summary>Gets a value indicating whether the four finder patterns were located and geometry verified.</summary>
    public required bool Registered { get; init; }

    /// <summary>Gets a value indicating whether the beacon block is black (frame id even); null when not registered.</summary>
    public bool? BeaconIsBlack { get; init; }

    /// <summary>Gets the recovered frame header, when readable.</summary>
    public FrameHeader? Header { get; init; }
}
