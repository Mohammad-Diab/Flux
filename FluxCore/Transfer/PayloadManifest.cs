using System.IO;
using System.Text.Json;
using FluxCore.Framing;

namespace FluxCore.Transfer;

/// <summary>
/// Identity of a compressed payload, shared by every render variant beneath it. Keyed by source
/// content + compression only — never by tile/colour/ECC — so changing format settings reuses this
/// payload and re-renders only the frames. Fields after <c>PayloadLength</c> were added later and
/// default to null/zero when reading manifests written by an earlier version.
/// </summary>
internal sealed record PayloadManifest(
    byte FormatVersion,
    string PayloadSignatureHex,
    PayloadType PayloadType,
    string PayloadSha256Hex,
    long PayloadLength,
    string? SourcePath = null,
    string? DisplayName = null,
    SourceKind SourceKind = SourceKind.File,
    DateTimeOffset? CreatedUtc = null)
{
    public static PayloadManifest? TryRead(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PayloadManifest>(File.ReadAllText(manifestPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
