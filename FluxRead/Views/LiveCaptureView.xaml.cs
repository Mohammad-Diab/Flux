using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Flux.Ui.Controls;
using Flux.Ui.Services;
using FluxCore.Imaging;
using FluxCore.Transfer;
using FluxRead.Interop;
using FluxRead.Services;
using FluxRead.ViewModels;

namespace FluxRead.Views;

/// <summary>
/// Live optical-capture screen. Owns the Win32-coupled setup and loop orchestration (region
/// selection, F8 calibration, live previews, running the loop in a mini window) and pushes
/// status into <see cref="LiveCaptureViewModel"/>.
/// </summary>
public partial class LiveCaptureView : UserControl
{
    private const int CalibrationCropWidth = 220;
    private const int CalibrationCropHeight = 90;

    private readonly LiveCaptureViewModel _vm;
    private readonly DecodePipelineService _pipeline;
    private readonly DialogService _dialogs;
    private readonly ScreenRegionCapture _previewCapture = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly Stopwatch _transferWatch = new();

    private Int32Rect _region;
    private (int X, int Y)? _nextPoint;
    private PointNextClicker? _clicker;
    private CaptureLoopService? _loop;
    private CancellationTokenSource? _cts;

    public LiveCaptureView(DecodePipelineService pipeline, DialogService dialogs)
    {
        _pipeline = pipeline;
        _dialogs = dialogs;
        _vm = new LiveCaptureViewModel();
        DataContext = _vm;
        InitializeComponent();

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _previewTimer.Tick += (_, _) => RefreshPreviews();

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => UpdateTiming();
        Unloaded += (_, _) => { _previewTimer.Stop(); _elapsedTimer.Stop(); };

        // Recovery is user-paced (they navigate the sender), so freeze elapsed/speed/ETA on entry.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LiveCaptureViewModel.IsRecovering) && _vm.IsRecovering)
            {
                _elapsedTimer.Stop();
                _transferWatch.Stop();
            }
        };

        // Keep the activity log scrolled to the newest line.
        _vm.Log.CollectionChanged += (_, _) =>
        {
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        };
    }

    private void OnSelectRegion(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var selector = new RegionSelectorWindow { Owner = owner };
        if (owner is not null)
            owner.WindowState = WindowState.Minimized;

        bool confirmed = selector.ShowDialog() == true && selector.Region is not null;
        if (owner is not null)
            owner.WindowState = WindowState.Normal;

        if (!confirmed)
            return;

        _region = selector.Region!.Value;
        _vm.HasRegion = true;
        _vm.RegionText = $"Region: {_region.Width}×{_region.Height} at ({_region.X},{_region.Y})";
        StartPreview();
    }

    private void OnCalibrate(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (owner is null)
            return;

        var hotkey = new HotkeyListener(owner);
        hotkey.Pressed += (_, _) =>
        {
            NativeMethods.GetCursorPos(out var pos);
            _nextPoint = (pos.X, pos.Y);
            _vm.HasCalibration = true;
            _vm.CalibrationText = $"Next button at ({pos.X},{pos.Y})";
            hotkey.Dispose();
            StartPreview();
        };
        hotkey.Arm();
        _vm.CalibrationText = "Hover over the Client's NEXT button, then press F8…";
    }

    private void StartPreview()
    {
        if (!_vm.IsRunning && (_vm.HasRegion || _nextPoint is not null))
        {
            RefreshPreviews();
            _previewTimer.Start();
        }
    }

    private void RefreshPreviews()
    {
        try
        {
            if (_vm.HasRegion)
            {
                using var region = _previewCapture.Capture(_region);
                _vm.RegionPreview = BitmapConverter.ToBitmapSource(region);
            }

            if (_nextPoint is { } p)
            {
                var crop = new Int32Rect(
                    p.X - CalibrationCropWidth / 2, p.Y - CalibrationCropHeight / 2,
                    CalibrationCropWidth, CalibrationCropHeight);
                using var preview = _previewCapture.Capture(crop);
                _vm.CalibrationPreview = BitmapConverter.ToBitmapSource(preview);
            }
        }
        catch
        {
            // Preview is best-effort; ignore transient capture errors.
        }
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_nextPoint is not { } point || !_vm.HasRegion)
            return;

        _previewTimer.Stop();
        _vm.RegionPreview = null;
        _vm.CalibrationPreview = null;

        var owner = Window.GetWindow(this)!;
        var handle = new WindowInteropHelper(owner).Handle;
        WindowPlacement.EnsureOutsideRegion(handle, _region);

        _cts = new CancellationTokenSource();
        _vm.IsRunning = true;
        _vm.IsPaused = false;
        _vm.TransferProgress = 0;
        _vm.ElapsedText = "";
        _vm.EtaText = "";
        _vm.Log.Clear();
        _vm.AddLog("Starting optical transfer…");
        _transferWatch.Restart();
        _elapsedTimer.Start();

        var capture = new RegionScreenCapture(_region);
        _clicker = new PointNextClicker(point);
        // Poll more frequently (so a quick advance is caught fast) while keeping roughly the same
        // ~1.8s budget before a re-click — re-clicking too early would over-advance and skip a frame.
        var options = new CaptureLoopOptions(
            PollIntervalMs: 100,
            MaxPollsPerClick: 18,
            MaxReclicks: 5,
            StabilityMaxAttempts: 16,
            StabilityIntervalMs: 60);
        _loop = new CaptureLoopService(capture, _clicker, ColorMap.Default, options);
        var progress = new Progress<LoopStatus>(_vm.Apply);

        var mini = new MiniCaptureWindow(_vm, TogglePause, () => _cts.Cancel()) { Owner = owner };
        owner.Hide();
        mini.Show();

        try
        {
            var report = await Task.Run(() => _loop.RunAsync(progress, ResolveStallAsync, _cts.Token));
            await HandleReportAsync(report);
        }
        catch (Exception ex)
        {
            _vm.AddLog($"Error: {ex.Message}");
            _vm.StateText = "Failed";
        }
        finally
        {
            TaskbarProgress.Current.Clear();
            _elapsedTimer.Stop();
            _transferWatch.Stop();
            _vm.IsRunning = false;
            _loop = null;
            _clicker = null;
            mini.Close();
            owner.Show();
            owner.Activate();
        }
    }

    // Ticks once a second during a transfer: elapsed wall-clock and a frames-based ETA.
    private void UpdateTiming()
    {
        var elapsed = _transferWatch.Elapsed;
        _vm.ElapsedText = "Elapsed " + FormatSpan(elapsed);

        int received = _vm.ReceivedCount, expected = _vm.ExpectedCount;
        _vm.SpeedText = received > 0 && elapsed.TotalSeconds > 0
            ? Flux.Ui.ByteFormat.Rate(_vm.ReceivedBytes / elapsed.TotalSeconds)
            : "";

        if (received > 0 && expected > 0 && received < expected)
        {
            double perFrame = elapsed.TotalSeconds / received;
            _vm.EtaText = "~" + FormatSpan(TimeSpan.FromSeconds(perFrame * (expected - received))) + " left";
        }
        else
        {
            _vm.EtaText = "";
        }
    }

    private static string FormatSpan(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes:D2}:{t.Seconds:D2}";

    private void TogglePause()
    {
        if (_loop is null)
            return;

        if (_loop.IsPaused)
        {
            _loop.Resume();
            _transferWatch.Start();
            _vm.IsPaused = false;
            _vm.AddLog("Resumed.");
        }
        else
        {
            _loop.Pause();
            _transferWatch.Stop();
            _vm.IsPaused = true;
            _vm.AddLog("Paused.");
        }
    }

    private Task<StallResolution> ResolveStallAsync(CancellationToken cancellationToken) =>
        Dispatcher.InvokeAsync(() =>
        {
            var result = MessageBox.Show(
                "The frame is stuck. Yes = retry the click · No = recalibrate the Next button · Cancel = abort.",
                "Transfer stalled", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            return result switch
            {
                MessageBoxResult.Yes => StallResolution.Retry,
                MessageBoxResult.No => Recalibrate(),
                _ => StallResolution.Abort,
            };
        }).Task;

    private StallResolution Recalibrate()
    {
        var owner = Window.GetWindow(this)!;
        using var hotkey = new HotkeyListener(owner);
        var tcs = new TaskCompletionSource<(int X, int Y)>();
        hotkey.Pressed += (_, _) =>
        {
            NativeMethods.GetCursorPos(out var pos);
            tcs.TrySetResult((pos.X, pos.Y));
        };
        hotkey.Arm();

        MessageBox.Show("Hover over the Client's NEXT button, press F8, then click OK.", "Recalibrate");
        if (tcs.Task.IsCompleted)
        {
            _nextPoint = tcs.Task.Result;
            var (x, y) = tcs.Task.Result;
            if (_clicker is not null)
                _clicker.Point = (x, y);
            _vm.CalibrationText = $"Next button at ({x},{y})";
        }

        return StallResolution.Recalibrate;
    }

    private async Task HandleReportAsync(TransferReport report)
    {
        _vm.AddLog(report.Summary());

        if (report.State != CaptureLoopState.Complete || report.Assembler is null || report.Metadata is null)
        {
            report.Assembler?.Dispose();
            return;
        }

        try
        {
            var metadata = report.Metadata;
            bool isArchive = metadata.PayloadType != FluxCore.Framing.PayloadType.Raw;

            // Don't count time spent in the save dialog; resume for the decompress that follows.
            _transferWatch.Stop();
            string? target = isArchive
                ? _dialogs.PickFolder("Choose a folder to extract into")
                : _dialogs.PickSaveFile("Save decoded file", metadata.OriginalName);

            if (target is null)
            {
                _vm.AddLog("Save cancelled.");
                return;
            }

            _transferWatch.Start();

            IProgress<int>? progress = null;
            if (isArchive)
            {
                _vm.StateText = "Decompressing…";
                _vm.IsDecompressing = true;
                progress = new Progress<int>(p =>
                {
                    _vm.DecompressProgress = p / 100.0;
                    _vm.StateText = $"Decompressing… {p}%";
                    TaskbarProgress.Current.Report(p / 100.0);
                });
            }

            await _pipeline.SaveAsync(report.Assembler, metadata, target, progress);
            _vm.IsDecompressing = false;
            _vm.AddLog($"Saved to {target}");
            _vm.StateText = "Saved";
            _dialogs.OpenInExplorer(target);
        }
        finally
        {
            _vm.IsDecompressing = false;
            report.Assembler.Dispose();
        }
    }

}
