using FluxCore.Ecc;

namespace FluxCore.Transfer;

/// <summary>
/// Options controlling how a source is encoded into frames.
/// </summary>
/// <param name="EccLevel">Error-correction level for payload frames (frame 0 always uses Max).</param>
/// <param name="Compress">Whether to 7z-compress the source. Folders are always compressed.</param>
public sealed record EncodeOptions(EccLevel EccLevel = EccLevel.Medium, bool Compress = true);
