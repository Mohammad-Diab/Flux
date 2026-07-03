using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FluxRead.Services;

/// <summary>
/// Service for capturing screenshots on Windows.
/// </summary>
public class ScreenCaptureService
{
    private readonly ILogger<ScreenCaptureService>? _logger;
 private System.Threading.Timer? _captureTimer;
    private bool _isCapturing;
    private int _captureCount;
    private string? _captureFolder;

    public event EventHandler<ScreenshotCapturedEventArgs>? ScreenshotCaptured;
    public event EventHandler<CaptureErrorEventArgs>? CaptureError;

    public bool IsCapturing => _isCapturing;
    public int CaptureCount => _captureCount;

 public ScreenCaptureService(ILogger<ScreenCaptureService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Starts periodic screenshot capture.
    /// </summary>
    /// <param name="intervalSeconds">Interval between captures in seconds.</param>
    /// <param name="captureFolder">Folder to save captured screenshots.</param>
    public void StartCapture(double intervalSeconds, string captureFolder)
    {
        if (_isCapturing)
  {
            _logger?.LogWarning("Capture already in progress");
            return;
        }

        _captureFolder = captureFolder;
        Directory.CreateDirectory(captureFolder);

        _isCapturing = true;
        _captureCount = 0;

        _logger?.LogInformation("Starting screenshot capture every {Interval}s to {Folder}", 
          intervalSeconds, captureFolder);

        // Immediate first capture
        CaptureScreenshot();

        // Schedule periodic captures
  var interval = TimeSpan.FromSeconds(intervalSeconds);
        _captureTimer = new System.Threading.Timer(
    _ => CaptureScreenshot(),
            null,
      interval,
            interval);
    }

    /// <summary>
    /// Stops the periodic capture.
    /// </summary>
    public void StopCapture()
    {
        if (!_isCapturing)
          return;

 _isCapturing = false;
        _captureTimer?.Dispose();
        _captureTimer = null;

        _logger?.LogInformation("Screenshot capture stopped. Total captures: {Count}", _captureCount);
    }

    /// <summary>
    /// Captures a single screenshot.
    /// </summary>
    private void CaptureScreenshot()
    {
        try
        {
      var bounds = GetPrimaryScreenBounds();
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
          using var graphics = Graphics.FromImage(bitmap);

     // Capture the screen
            graphics.CopyFromScreen(
             bounds.X, 
       bounds.Y, 
    0, 
       0, 
        bounds.Size, 
     CopyPixelOperation.SourceCopy);

            // Save to file
            var filename = $"capture_{_captureCount:D6}.png";
        var filepath = Path.Combine(_captureFolder!, filename);

            using var memoryStream = new MemoryStream();
      bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
 var imageData = memoryStream.ToArray();

         File.WriteAllBytes(filepath, imageData);

         _captureCount++;

            _logger?.LogDebug("Captured screenshot {Count}: {File}", _captureCount, filename);

// Raise event
    ScreenshotCaptured?.Invoke(this, new ScreenshotCapturedEventArgs
     {
    CaptureIndex = _captureCount - 1,
                FilePath = filepath,
    ImageData = imageData
    });
        }
        catch (Exception ex)
        {
       _logger?.LogError(ex, "Failed to capture screenshot");
      CaptureError?.Invoke(this, new CaptureErrorEventArgs
     {
                Error = ex.Message
  });
      }
    }

    /// <summary>
    /// Gets the bounds of the primary screen.
    /// </summary>
    private Rectangle GetPrimaryScreenBounds()
    {
#if WINDOWS
        // Use Windows-specific API for accurate screen bounds
        return new Rectangle(
            0, 
      0, 
            GetSystemMetrics(SM_CXSCREEN),
      GetSystemMetrics(SM_CYSCREEN));
#else
        // Fallback for other platforms
      return new Rectangle(0, 0, 1920, 1080);
#endif
    }

#if WINDOWS
    // Windows API for getting screen dimensions
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
#endif
}

public class ScreenshotCapturedEventArgs : EventArgs
{
    public int CaptureIndex { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
}

public class CaptureErrorEventArgs : EventArgs
{
    public string Error { get; set; } = string.Empty;
}
