namespace FluxCore.Transfer;

/// <summary>
/// Canonical file and folder names inside an encode session. A session is two-level: one payload
/// directory (named by the payload signature) holds the shared <c>payload.dat</c> plus a
/// <c>renders/</c> subtree, and each render variant (named by the render signature) holds its own
/// frames and manifest — so re-rendering a source at new settings reuses the payload.
/// </summary>
internal static class SessionLayout
{
    public const string PayloadFileName = "payload.dat";
    public const string PayloadManifestFileName = "payload.json";
    public const string RendersFolderName = "renders";
    public const string RenderManifestFileName = "manifest.json";
    public const string FramesFolderName = "frames";
    public const string FrameSearchPattern = "frame_*.png";
}
