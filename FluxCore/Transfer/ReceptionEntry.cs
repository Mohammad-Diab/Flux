using FluxCore.Framing;

namespace FluxCore.Transfer;

/// <summary>A received (or partially received) transfer surfaced in FluxRead's history list.</summary>
/// <param name="SessionDirectory">Root session folder.</param>
/// <param name="DisplayName">Original file or folder name.</param>
/// <param name="PayloadType">Raw (a single file) or 7z (a compressed file or folder).</param>
/// <param name="PayloadLength">Transferred payload size in bytes.</param>
/// <param name="OriginalLength">Original uncompressed size in bytes.</param>
/// <param name="TotalFrames">Total frames including frame 0.</param>
/// <param name="ReceivedFrames">Payload frames received so far (excludes frame 0).</param>
/// <param name="CreatedUtc">When the reception was first seen.</param>
/// <param name="CompletedUtc">When the reception finished, if it did.</param>
/// <param name="IsComplete">Whether the payload was received, verified, and saved.</param>
/// <param name="SavedPath">Where the completed output was saved (null while incomplete).</param>
public sealed record ReceptionEntry(
    string SessionDirectory,
    string DisplayName,
    PayloadType PayloadType,
    long PayloadLength,
    long OriginalLength,
    uint TotalFrames,
    int ReceivedFrames,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? CompletedUtc,
    bool IsComplete,
    string? SavedPath)
{
    /// <summary>Gets the number of payload frames expected (total minus frame 0).</summary>
    public uint ExpectedPayloadFrames => TotalFrames == 0 ? 0 : TotalFrames - 1;
}
