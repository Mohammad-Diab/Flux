namespace FluxCore.Transfer;

/// <summary>Canonical file names inside a FluxRead reception session directory.</summary>
internal static class ReceptionLayout
{
    public const string ManifestFileName = "manifest.json";
    public const string PayloadFileName = "payload.bin";
    public const string ReceivedIndexFileName = "received.idx";
}
