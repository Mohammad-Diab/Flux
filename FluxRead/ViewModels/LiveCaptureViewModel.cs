using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FluxCore.Transfer;

namespace FluxRead.ViewModels;

/// <summary>
/// Bindable status for the live optical-capture screen. The Win32-coupled orchestration
/// (region selection, F8 calibration, running the loop) lives in the view code-behind and
/// pushes updates here.
/// </summary>
public partial class LiveCaptureViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _hasRegion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _hasCalibration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanConfigure))]
    private bool _isRunning;

    [ObservableProperty]
    private string _regionText = "No region selected.";

    [ObservableProperty]
    private string _calibrationText = "Next button not calibrated.";

    [ObservableProperty]
    private string _stateText = "Idle";

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private BitmapSource? _lastThumbnail;

    [ObservableProperty]
    private BitmapSource? _regionPreview;

    [ObservableProperty]
    private BitmapSource? _calibrationPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseLabel))]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isDecompressing;

    [ObservableProperty]
    private double _decompressProgress;

    [ObservableProperty]
    private double _transferProgress;

    [ObservableProperty]
    private string _elapsedText = "";

    [ObservableProperty]
    private string _etaText = "";

    /// <summary>Payload frames received so far (for the elapsed/ETA estimate).</summary>
    public int ReceivedCount { get; private set; }

    /// <summary>Total payload frames expected (for the elapsed/ETA estimate).</summary>
    public int ExpectedCount { get; private set; }

    /// <summary>Gets the label for the pause/resume toggle.</summary>
    public string PauseLabel => IsPaused ? "Resume" : "Pause";

    /// <summary>Gets a value indicating whether the transfer can be started.</summary>
    public bool CanStart => HasRegion && HasCalibration && !IsRunning;

    /// <summary>Gets a value indicating whether setup controls are enabled.</summary>
    public bool CanConfigure => !IsRunning;

    /// <summary>Gets the capped scrolling log.</summary>
    public ObservableCollection<string> Log { get; } = [];

    /// <summary>Appends a time-stamped line to the log, capping its length.</summary>
    /// <param name="message">Log line.</param>
    public void AddLog(string message)
    {
        Log.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (Log.Count > 200)
            Log.RemoveAt(0);
    }

    /// <summary>Applies a loop status snapshot to the bindable state.</summary>
    /// <param name="status">Loop status.</param>
    public void Apply(LoopStatus status)
    {
        StateText = FriendlyState(status.State);

        if (status.TotalFrames > 0)
        {
            ExpectedCount = status.TotalFrames - 1;
            ReceivedCount = status.ReceivedFrames;
            ProgressText = $"{ReceivedCount}/{ExpectedCount} frames · last id {status.LastFrameId} · re-clicks {status.Reclicks}";
            TransferProgress = ExpectedCount > 0 ? (double)ReceivedCount / ExpectedCount : 0;
        }
        else
        {
            ProgressText = status.Message;
        }

        // The message is already event-specific ("Received frame 12 (12/299).", stalls, etc.) —
        // log it verbatim; routine ticks send an empty message and are skipped.
        if (!string.IsNullOrEmpty(status.Message))
            AddLog(status.Message);

        if (status.LastFramePng is { } png)
            LastThumbnail = Decode(png);
    }

    /// <summary>Maps a loop state to a plain-English label for the UI.</summary>
    private static string FriendlyState(CaptureLoopState state) => state switch
    {
        CaptureLoopState.WaitingForFrame0 => "Looking for the first frame…",
        CaptureLoopState.ClickingNext => "Clicking Next…",
        CaptureLoopState.WaitingForAdvance => "Waiting for the next frame…",
        CaptureLoopState.Stalled => "Stalled — needs attention",
        CaptureLoopState.Reassembling => "Reassembling & verifying…",
        CaptureLoopState.Complete => "Complete",
        CaptureLoopState.Failed => "Failed",
        CaptureLoopState.Cancelled => "Cancelled",
        _ => state.ToString(),
    };

    private static BitmapSource Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
