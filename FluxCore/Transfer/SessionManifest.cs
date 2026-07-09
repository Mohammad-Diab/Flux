using System.IO;
using System.Text.Json;
using FluxCore.Framing;

namespace FluxCore.Transfer;

/// <summary>
/// Persisted description of an encode session: enough to reuse the payload on resume and to list
/// the cast in FluxCast's history. Fields after <c>PayloadLength</c> were added later and default
/// to null/zero when reading manifests written by an earlier version.
/// </summary>
internal sealed record SessionManifest(
    byte FormatVersion,
    string SignatureHex,
    PayloadType PayloadType,
    string PayloadSha256Hex,
    long PayloadLength,
    string? SourcePath = null,
    string? DisplayName = null,
    SourceKind SourceKind = SourceKind.File,
    uint TotalFrames = 0,
    DateTimeOffset? CreatedUtc = null)
{
    public static SessionManifest? TryRead(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(manifestPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
