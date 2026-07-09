namespace FluxCore.Transfer;

/// <summary>A past cast surfaced in FluxCast's history list.</summary>
/// <param name="SessionDirectory">Root session folder.</param>
/// <param name="FramesDirectory">Folder holding the frame PNGs.</param>
/// <param name="DisplayName">Source file or folder name.</param>
/// <param name="SourcePath">Original source path, if known (it may no longer exist on disk).</param>
/// <param name="SourceKind">Whether the source was a file or a folder.</param>
/// <param name="TotalFrames">Total frames including frame 0.</param>
/// <param name="PayloadLength">Transfer payload size in bytes.</param>
/// <param name="CreatedUtc">When the cast was first encoded.</param>
/// <param name="IsComplete">Whether every frame is present on disk (so it can be re-presented as-is).</param>
public sealed record CastHistoryEntry(
    string SessionDirectory,
    string FramesDirectory,
    string DisplayName,
    string? SourcePath,
    SourceKind SourceKind,
    uint TotalFrames,
    long PayloadLength,
    DateTimeOffset CreatedUtc,
    bool IsComplete);
