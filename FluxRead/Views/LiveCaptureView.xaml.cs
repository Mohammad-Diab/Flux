using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Flux.Ui.Controls;
using Flux.Ui.Services;
using FluxCore.Decoding;
using FluxCore.Imaging;
using FluxCore.Transfer;
using FluxRead.Interop;
using FluxRead.Services;
using FluxRead.ViewModels;
using SkiaSharp;

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
    private readonly ReceptionHistoryService _history;
    private readonly ScreenRegionCapture _previewCapture = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly Stopwatch _transferWatch = new();

    private Int32Rect _region;
    private (int X, int Y)? _nextPoint;
    private RegionScreenCapture? _captureSource;
    private PointNextClicker? _clicker;
    private CaptureLoopService? _loop;
    private MiniCaptureWindow? _mini;
    private CancellationTokenSource? _cts;

    public LiveCaptureView(DecodePipelineService pipeline, DialogService dialogs, ReceptionHistoryService history)
    {
        _pipeline = pipeline;
        _dialogs = dialogs;
        _history = history;
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

    private async void OnDetectRegion(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (owner is null)
            return;

        _vm.RegionText = "Scanning the screen for a frame…";
        owner.WindowState = WindowState.Minimized;
        await Task.Delay(350);

        var virtualScreen = DpiUtil.GetVirtualScreenPhysical();
        using var shot = _previewCapture.Capture(virtualScreen);
        owner.WindowState = WindowState.Normal;

        var regions = await Task.Run(() => new FrameLocator(ColorMap.Default).Locate(shot));

        if (regions.Count == 0)
        {
            _vm.RegionText = "No frame found — select the region manually.";
            SelectRegionManually(owner);
            return;
        }

        FrameRegion chosen;
        if (regions.Count == 1)
        {
            chosen = regions[0];
        }
        else
        {
            var picker = new FramePickerWindow(shot, regions) { Owner = owner };
            if (picker.ShowDialog() != true || picker.SelectedIndex is not { } index)
            {
                _vm.RegionText = "No region selected.";
                return;
            }

            chosen = regions[index];
        }

        ApplyRegion(new Int32Rect(virtualScreen.X + chosen.X, virtualScreen.Y + chosen.Y, chosen.Width, chosen.Height));

        // Reuse the same screenshot to also find the Next button in the toolbar below the frame.
        _vm.CalibrationText = "Looking for the Next button…";
        if (!await TryAutoNextAsync(shot, virtualScreen, chosen))
            _vm.CalibrationText = "Next button not found — calibrate it with F8.";
    }

    private async void OnDetectNext(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (owner is null)
            return;

        _vm.CalibrationText = "Looking for the Next button…";
        owner.WindowState = WindowState.Minimized;
        await Task.Delay(350);

        var virtualScreen = DpiUtil.GetVirtualScreenPhysical();
        using var shot = _previewCapture.Capture(virtualScreen);
        owner.WindowState = WindowState.Normal;

        bool found = _vm.HasRegion
            ? await TryAutoNextAsync(shot, virtualScreen, ToShotRegion(virtualScreen))
            : await OcrNextLocator.FindNextAsync(shot, virtualScreen.X, virtualScreen.Y) is { } p && ApplyNextPoint(p);

        if (!found)
            _vm.CalibrationText = "Next button not found — calibrate it with F8.";
    }

    private FrameRegion ToShotRegion(Int32Rect virtualScreen) =>
        new(_region.X - virtualScreen.X, _region.Y - virtualScreen.Y, _region.Width, _region.Height, null);

    private async Task<bool> TryAutoNextAsync(SKBitmap shot, Int32Rect virtualScreen, FrameRegion frame)
    {
        int sx = Math.Max(0, frame.X - frame.Width / 4);
        int sy = frame.Y + frame.Height;
        int sw = Math.Min(shot.Width - sx, frame.Width + frame.Width / 2);
        int sh = Math.Min(shot.Height - sy, frame.Height);
        if (sw <= 0 || sh <= 0)
            return false;

        using var strip = new SKBitmap(sw, sh, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(strip))
            canvas.DrawBitmap(shot, new SKRect(sx, sy, sx + sw, sy + sh), new SKRect(0, 0, sw, sh));

        return await OcrNextLocator.FindNextAsync(strip, virtualScreen.X + sx, virtualScreen.Y + sy) is { } point
            && ApplyNextPoint(point);
    }

    private void OnSelectRegionManual(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } owner)
            SelectRegionManually(owner);
    }

    private void SelectRegionManually(Window owner)
    {
        var selector = new RegionSelectorWindow { Owner = owner };
        owner.WindowState = WindowState.Minimized;
        bool confirmed = selector.ShowDialog() == true && selector.Region is not null;
        owner.WindowState = WindowState.Normal;

        if (confirmed)
            ApplyRegion(selector.Region!.Value);
    }

    private void ApplyRegion(Int32Rect region)
    {
        _region = region;
        _vm.HasRegion = true;
        _vm.RegionText = $"Region: {region.Width}×{region.Height} at ({region.X},{region.Y})";
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
            ApplyNextPoint((pos.X, pos.Y));
            hotkey.Dispose();
        };
        hotkey.Arm();
        _vm.CalibrationText = "Hover over the Client's NEXT button, then press F8…";
    }

    private bool ApplyNextPoint((int X, int Y) point)
    {
        _nextPoint = point;
        if (_clicker is not null)
            _clicker.Point = point;   // retarget a running loop after a stall recalibration
        _vm.HasCalibration = true;
        _vm.CalibrationText = $"Next button at ({point.X},{point.Y})";
        StartPreview();
        return true;
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

        _captureSource = new RegionScreenCapture(_region);
        _clicker = new PointNextClicker(point);
        // Poll more frequently (so a quick advance is caught fast) while keeping roughly the same
        // ~1.8s budget before a re-click — re-clicking too early would over-advance and skip a frame.
        var options = new CaptureLoopOptions(
            PollIntervalMs: 100,
            MaxPollsPerClick: 18,
            MaxReclicks: 5,
            StabilityMaxAttempts: 16,
            StabilityIntervalMs: 60);
        _loop = new CaptureLoopService(_captureSource, _clicker, ColorMap.Default, options,
            assemblerFactory: metadata => _history.OpenAssembler(ShellViewModel.SessionRoot, metadata));
        var progress = new Progress<LoopStatus>(_vm.Apply);

        _mini = new MiniCaptureWindow(_vm, TogglePause, () => _cts.Cancel()) { Owner = owner };
        owner.Hide();
        _mini.Show();

        try
        {
            var report = await Task.Run(() => _loop.RunAsync(progress, ResolveStallAsync, _cts.Token, ResolveResumeAsync));
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
            _captureSource = null;
            _mini?.Close();
            _mini = null;
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
        Dispatcher.InvokeAsync(async () =>
        {
            var dialog = new StallDialog(
                "The sender stopped advancing after several tries. Resume, re-find the Next button, "
                + "or re-detect the frame — then FluxRead keeps going.") { Owner = _mini };
            dialog.ShowDialog();

            switch (dialog.Choice)
            {
                case StallChoice.RecalibrateNext:
                    await RecalibrateNextAsync();
                    return StallResolution.Retry;
                case StallChoice.AdjustRegion:
                    await AdjustRegionAsync();
                    return StallResolution.Retry;
                case StallChoice.Cancel:
                    return StallResolution.Abort;
                default:
                    return StallResolution.Retry;
            }
        }).Task.Unwrap();

    private Task<ResumeMode> ResolveResumeAsync(ResumeContext context, CancellationToken cancellationToken) =>
        Dispatcher.InvokeAsync(() =>
        {
            var dialog = new ResumeDialog(context.ReceivedFrames, context.ExpectedPayloadFrames, context.FirstMissingFrameId)
            {
                Owner = _mini,
            };
            dialog.ShowDialog();

            switch (dialog.Choice)
            {
                case ResumeChoice.Automatic:
                    _vm.AddLog($"Resuming — skipping ahead to frame {context.FirstMissingFrameId}.");
                    return ResumeMode.Automatic;

                case ResumeChoice.Manual:
                    var manual = new ManualResumeDialog(context.FirstMissingFrameId) { Owner = _mini };
                    manual.ShowDialog();
                    if (manual.Continued)
                    {
                        _vm.AddLog($"Resuming manually from frame {context.FirstMissingFrameId}.");
                        return ResumeMode.Manual;
                    }

                    _cts?.Cancel();
                    return ResumeMode.Automatic;

                case ResumeChoice.StartOver:
                    _vm.AddLog("Starting over — discarding received frames.");
                    return ResumeMode.StartOver;

                default:
                    // Cancelled: return any mode; the seek aborts on the cancelled token, keeping received frames.
                    _cts?.Cancel();
                    return ResumeMode.Automatic;
            }
        }).Task;

    private async Task<SKBitmap> CaptureWithMiniHiddenAsync(Int32Rect virtualScreen)
    {
        if (_mini is not null)
            _mini.WindowState = WindowState.Minimized;
        await Task.Delay(350);
        var shot = _previewCapture.Capture(virtualScreen);
        if (_mini is not null)
            _mini.WindowState = WindowState.Normal;
        return shot;
    }

    private async Task RecalibrateNextAsync()
    {
        _vm.AddLog("Re-finding the Next button…");
        var virtualScreen = DpiUtil.GetVirtualScreenPhysical();
        using var shot = await CaptureWithMiniHiddenAsync(virtualScreen);

        if (!await TryAutoNextAsync(shot, virtualScreen, ToShotRegion(virtualScreen)))
        {
            _vm.AddLog("Couldn't find Next automatically — use F8 to calibrate.");
            RecalibrateWithF8();
        }
    }

    private void RecalibrateWithF8()
    {
        var host = _mini ?? Window.GetWindow(this);
        if (host is null)
            return;

        using var hotkey = new HotkeyListener(host);
        var tcs = new TaskCompletionSource<(int X, int Y)>();
        hotkey.Pressed += (_, _) => { NativeMethods.GetCursorPos(out var pos); tcs.TrySetResult((pos.X, pos.Y)); };
        hotkey.Arm();

        MessageBox.Show(host, "Hover over the sender's Next button, press F8, then click OK.", "Recalibrate");
        if (tcs.Task.IsCompleted)
            ApplyNextPoint(tcs.Task.Result);
    }

    private async Task AdjustRegionAsync()
    {
        _vm.AddLog("Re-detecting the frame…");
        var virtualScreen = DpiUtil.GetVirtualScreenPhysical();
        using var shot = await CaptureWithMiniHiddenAsync(virtualScreen);

        var regions = await Task.Run(() => new FrameLocator(ColorMap.Default).Locate(shot));
        if (regions.Count == 0)
        {
            _vm.AddLog("No frame found — keeping the current region.");
            return;
        }

        FrameRegion chosen;
        if (regions.Count == 1)
        {
            chosen = regions[0];
        }
        else
        {
            var picker = new FramePickerWindow(shot, regions) { Owner = _mini };
            if (picker.ShowDialog() != true || picker.SelectedIndex is not { } index)
                return;
            chosen = regions[index];
        }

        _region = new Int32Rect(virtualScreen.X + chosen.X, virtualScreen.Y + chosen.Y, chosen.Width, chosen.Height);
        if (_captureSource is not null)
            _captureSource.Region = _region;
        _vm.RegionText = $"Region: {_region.Width}×{_region.Height} at ({_region.X},{_region.Y})";
        _vm.AddLog("Region updated.");
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

            // Verified and saved: retire the received buffer, keeping the manifest as a history record.
            if (report.Assembler.IsPersistent)
                _history.MarkComplete(Path.GetDirectoryName(report.Assembler.PayloadFilePath)!, target);

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
