namespace FluxCore.Transfer;

/// <summary>
/// Phase of an encode session.
/// </summary>
public enum EncodePhase
{
    /// <summary>Compressing the source into the transfer payload.</summary>
    Compressing,

    /// <summary>Rendering frame PNGs.</summary>
    RenderingFrames,

    /// <summary>All frames are on disk.</summary>
    Completed,
}

/// <summary>
/// Progress report for an encode session.
/// </summary>
/// <param name="Phase">Current phase.</param>
/// <param name="CompletedFrames">Frames confirmed on disk so far (rendering phase only).</param>
/// <param name="TotalFrames">Total frames in the transfer (rendering phase only).</param>
/// <param name="CompressionPercent">Compression progress 0-100 (compressing phase, 7z only; -1 if unknown).</param>
public sealed record EncodeProgress(
    EncodePhase Phase, int CompletedFrames = 0, int TotalFrames = 0, int CompressionPercent = -1);
