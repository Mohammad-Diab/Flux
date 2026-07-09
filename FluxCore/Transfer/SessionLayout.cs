namespace FluxCore.Transfer;

/// <summary>Canonical file and folder names inside an encode session directory.</summary>
internal static class SessionLayout
{
    public const string PayloadFileName = "payload.dat";
    public const string ManifestFileName = "manifest.json";
    public const string FramesFolderName = "frames";
    public const string FrameSearchPattern = "frame_*.png";
}
