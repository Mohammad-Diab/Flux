using System.IO;
using System.Text.Json;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;

namespace FluxCore.Transfer;

/// <summary>Lifecycle state of a reception session.</summary>
internal enum ReceptionStatus
{
    /// <summary>Frames are still being received; the session can be resumed.</summary>
    InProgress = 0,

    /// <summary>The payload was received, verified, and saved.</summary>
    Complete = 1,
}

/// <summary>
/// Persisted description of a FluxRead reception: enough to resume an interrupted transfer and to
/// list it in the receiver's history. Keyed on disk by the transfer's content signature, so a
/// returning transfer resolves to the same folder and resumes. Mirrors the sender's
/// <see cref="SessionManifest"/>.
/// </summary>
internal sealed record ReceptionManifest(
    byte FormatVersion,
    string SignatureHex,
    PayloadType PayloadType,
    string PayloadSha256Hex,
    long PayloadLength,
    string OriginalName,
    long OriginalLength,
    EccLevel EccLevel,
    uint TotalFrames,
    ReceptionStatus Status = ReceptionStatus.InProgress,
    string? SavedPath = null,
    DateTimeOffset? CreatedUtc = null,
    DateTimeOffset? CompletedUtc = null)
{
    /// <summary>Builds an in-progress manifest from freshly decoded frame-0 metadata.</summary>
    public static ReceptionManifest ForMetadata(MetadataPayload metadata, DateTimeOffset createdUtc) =>
        new(
            FrameFormat.Version,
            Convert.ToHexString(metadata.ContentSignature).ToLowerInvariant(),
            metadata.PayloadType,
            Sha256Helper.ToHexString(metadata.Sha256),
            metadata.PayloadLength,
            metadata.OriginalName,
            metadata.OriginalLength,
            metadata.EccLevel,
            metadata.TotalFrames,
            CreatedUtc: createdUtc);

    /// <summary>
    /// Determines whether persisted frames from this session are safe to reuse for an incoming
    /// transfer: the signature, hash, payload length, frame count, and ECC level (which fixes the
    /// per-frame byte offsets) must all match.
    /// </summary>
    public bool IsCompatibleWith(MetadataPayload metadata) =>
        SignatureHex == Convert.ToHexString(metadata.ContentSignature).ToLowerInvariant() &&
        PayloadSha256Hex == Sha256Helper.ToHexString(metadata.Sha256) &&
        PayloadLength == metadata.PayloadLength &&
        TotalFrames == metadata.TotalFrames &&
        EccLevel == metadata.EccLevel;

    public static ReceptionManifest? TryRead(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ReceptionManifest>(File.ReadAllText(manifestPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Write(string manifestPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(this));
    }
}
