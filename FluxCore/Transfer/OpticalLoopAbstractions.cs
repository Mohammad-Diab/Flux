using SkiaSharp;

namespace FluxCore.Transfer;

/// <summary>
/// Captures the calibrated screen region as a bitmap. Implemented in the Server app over GDI;
/// abstracted here so the capture loop can be driven by a fake in tests.
/// </summary>
public interface IScreenCapture
{
    /// <summary>Captures the current contents of the calibrated region.</summary>
    SKBitmap Capture();
}

/// <summary>
/// Synthesizes a click on the Client's Next button at the calibrated screen point.
/// </summary>
public interface INextClicker
{
    /// <summary>Clicks the Next button.</summary>
    void ClickNext();
}
