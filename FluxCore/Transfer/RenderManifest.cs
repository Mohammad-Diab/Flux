using System.IO;
using System.Text.Json;
using FluxCore.Ecc;
using FluxCore.Imaging;

namespace FluxCore.Transfer;

/// <summary>
/// One rendering of a payload at a specific format spec (grid, tile size, ECC, colour). Lives in a
/// render subfolder under its <see cref="PayloadManifest"/>; several variants can share one payload.
/// Carries the combined wire signature painted into frame 0 and the frame count, which depends on
/// the spec. Fields after <c>TotalFrames</c> default when reading an older manifest.
/// </summary>
internal sealed record RenderManifest(
    byte FormatVersion,
    string RenderSignatureHex,
    string CombinedSignatureHex,
    EccLevel EccLevel,
    int GridWidthTiles,
    int GridHeightTiles,
    int TilePixelSize,
    int ColorCount,
    uint TotalFrames,
    DateTimeOffset? CreatedUtc = null,
    PaletteKind PaletteKind = PaletteKind.Standard)
{
    public static RenderManifest? TryRead(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<RenderManifest>(File.ReadAllText(manifestPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
