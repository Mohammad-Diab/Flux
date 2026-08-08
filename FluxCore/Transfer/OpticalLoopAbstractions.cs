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

/// <summary>What happened when the loop asked for a Next click.</summary>
public enum NextClickOutcome
{
    /// <summary>The click was delivered to the sender.</summary>
    Clicked,

    /// <summary>Another window covers the Next button; nothing was clicked.</summary>
    Covered,

    /// <summary>The sender's window is minimized; nothing was clicked.</summary>
    Minimized,

    /// <summary>The sender's window no longer exists; nothing was clicked.</summary>
    WindowGone,
}

/// <summary>
/// Synthesizes a click on the Client's Next button at the calibrated screen point.
/// </summary>
public interface INextClicker
{
    /// <summary>Clicks the Next button when it is reachable; never clicks anything else.</summary>
    NextClickOutcome ClickNext();
}

/// <summary>
/// Re-finds the channel's moving parts mid-transfer without user interaction: the loop calls these
/// between automatic retries, before it ever pauses to ask the user. Each call is one bounded
/// attempt — false means "not found right now", never "wait".
/// </summary>
public interface ILoopRecalibrator
{
    /// <summary>Re-locates the sender's Next button and retargets the clicker; false when it can't be found.</summary>
    Task<bool> RecalibrateNextButtonAsync(CancellationToken cancellationToken);

    /// <summary>Re-locates the frame on screen and retargets the capture; false when no frame is found.</summary>
    Task<bool> RecalibrateFrameAsync(CancellationToken cancellationToken);
}
