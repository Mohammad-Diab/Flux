namespace FluxCore.Transfer;

/// <summary>
/// Outcome of an encode session.
/// </summary>
/// <param name="SessionDirectory">The render-variant folder (named by render signature), holding this rendering's frames + manifest.</param>
/// <param name="PayloadDirectory">The payload folder (named by payload signature) holding the shared payload.dat, above the render folder.</param>
/// <param name="FramesDirectory">Folder containing frame_NNNNNN.png files.</param>
/// <param name="TotalFrames">Total frames including frame 0.</param>
/// <param name="PayloadLength">Transfer payload length in bytes (compressed size for 7z).</param>
/// <param name="ContentSignature">32-byte combined transfer signature painted into frame 0.</param>
/// <param name="PayloadReused">Whether an existing compressed payload was reused (resume).</param>
/// <param name="FramesRendered">Frames actually rendered this run (0 = fully resumed).</param>
public sealed record EncodeSessionResult(
    string SessionDirectory,
    string PayloadDirectory,
    string FramesDirectory,
    uint TotalFrames,
    long PayloadLength,
    byte[] ContentSignature,
    bool PayloadReused,
    int FramesRendered);
