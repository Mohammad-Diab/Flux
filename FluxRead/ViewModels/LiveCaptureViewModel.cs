using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Flux.Ui.Controls;
using FluxCore.Transfer;
using FluxRead.Services;

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
    private string _receivedCountText = "";

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string _missingCountText = "";

    [ObservableProperty]
    private string _retryText = "";

    [ObservableProperty]
    private string _recoveryTitle = "";

    [ObservableProperty]
    private string _recoveryHint = "";

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
    private bool _isRecovering;

    [ObservableProperty]
    private string _missingFramesText = "";

    [ObservableProperty]
    private bool _isDecompressing;

    [ObservableProperty]
    private double _decompressProgress;

    [ObservableProperty]
    private double _transferProgress;

    [ObservableProperty]
    private string _elapsedText = "";

    [ObservableProperty]
    private string _speedText = "";

    [ObservableProperty]
    private string _etaText = "";

    /// <summary>Payload frames received so far (for the elapsed/ETA estimate).</summary>
    public int ReceivedCount { get; private set; }

    /// <summary>Total payload frames expected (for the elapsed/ETA estimate).</summary>
    public int ExpectedCount { get; private set; }

    /// <summary>Payload bytes received so far (for the speed readout).</summary>
    public long ReceivedBytes { get; private set; }

    /// <summary>Gets the label for the pause/resume toggle.</summary>
    public string PauseLabel => IsPaused ? "Resume" : "Pause";

    /// <summary>Gets a value indicating whether the transfer can be started.</summary>
    public bool CanStart => HasRegion && HasCalibration && !IsRunning;

    /// <summary>Gets a value indicating whether setup controls are enabled.</summary>
    public bool CanConfigure => !IsRunning;

    /// <summary>Gets the capped scrolling log.</summary>
    public ObservableCollection<string> Log { get; } = [];

    private string _lastLoggedMessage = "";

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
        IsRecovering = status.State == CaptureLoopState.RecoveringGaps;
        bool stuck = status.StuckFrameId > 0;
        StateText = stuck ? "Waiting for the sender…" : FriendlyState(status.State);

        if (IsRecovering && stuck)
        {
            RecoveryTitle = "No new frames arriving";
            RecoveryHint = $"Frame {status.StuckFrameId} hasn't arrived after several tries. Make sure the sender " +
                $"is running and showing frame {status.StuckFrameId} — if it stopped, reopen it and go to that frame. " +
                "FluxRead resumes automatically once frames appear.";
            MissingFramesText = "";
        }
        else if (IsRecovering && status.MissingFrameIds is { } missing)
        {
            RecoveryTitle = "Missing frames";
            RecoveryHint = "On the sender, use Back or “go to frame” to re-show each frame below — FluxRead grabs them automatically.";
            MissingFramesText = FormatMissing(missing);
        }

        if (status.TotalFrames > 0)
        {
            ExpectedCount = status.TotalFrames - 1;
            ReceivedCount = status.ReceivedFrames;
            ReceivedBytes = status.ReceivedBytes;
            string size = status.TotalBytes > 0 ? $" ({Flux.Ui.ByteFormat.Bytes(status.TotalBytes)})" : "";
            ReceivedCountText = ReceivedCount.ToString();
            ProgressText = $"/{ExpectedCount} frames{size}";
            RetryText = status.Reclicks > 0 ? $"retrying ({status.Reclicks})" : "";
            MissingCountText = status.MissingFrames > 0 ? $"missing {status.MissingFrames}" : "";
            TransferProgress = ExpectedCount > 0 ? (double)ReceivedCount / ExpectedCount : 0;
        }
        else
        {
            ReceivedCountText = "";
            ProgressText = status.Message;
            RetryText = "";
            MissingCountText = "";
        }

        UpdateTaskbar(status.State);

        // Log event messages verbatim; skip empty ticks and consecutive repeats (e.g. a held frame-0 warning).
        if (!string.IsNullOrEmpty(status.Message) && status.Message != _lastLoggedMessage)
        {
            AddLog(status.Message);
            _lastLoggedMessage = status.Message;
        }

        if (status.LastFramePng is { } png)
            LastThumbnail = BitmapConverter.FromPng(png);
    }

    private void UpdateTaskbar(CaptureLoopState state)
    {
        switch (state)
        {
            case CaptureLoopState.Failed:
            case CaptureLoopState.Cancelled:
                TaskbarProgress.Current.Clear();
                break;
            case CaptureLoopState.WaitingForFrame0:
                TaskbarProgress.Current.Indeterminate();
                break;
            default:
                if (ExpectedCount > 0 && ReceivedCount > 0)
                    TaskbarProgress.Current.Report(TransferProgress);
                else
                    TaskbarProgress.Current.Indeterminate();
                break;
        }
    }

    /// <summary>Maps a loop state to a plain-English label for the UI.</summary>
    private static string FriendlyState(CaptureLoopState state) => state switch
    {
        CaptureLoopState.WaitingForFrame0 => "Looking for the first frame…",
        CaptureLoopState.ClickingNext => "Transferring…",
        CaptureLoopState.WaitingForAdvance => "Transferring…",
        CaptureLoopState.Stalled => "Stalled — needs attention",
        CaptureLoopState.RecoveringGaps => "Recovering missing frames…",
        CaptureLoopState.Reassembling => "Reassembling & verifying…",
        CaptureLoopState.Complete => "Complete",
        CaptureLoopState.Failed => "Failed",
        CaptureLoopState.Cancelled => "Cancelled",
        _ => state.ToString(),
    };

    private static string FormatMissing(IReadOnlyList<uint> missing)
    {
        if (missing.Count == 0)
            return "";

        const int max = 24;
        string head = string.Join(", ", missing.Take(max));
        return missing.Count > max ? $"{head} … (+{missing.Count - max} more)" : head;
    }
}
