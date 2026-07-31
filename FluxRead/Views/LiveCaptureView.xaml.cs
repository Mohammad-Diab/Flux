using System.Diagnostics;
using System.IO;
using Flux.Ui.Services;
using FluxCore.Decoding;
using FluxCore.Imaging;
using FluxCore.Transfer;
using FluxRead.Services;
using Flux.Ui.Interop;
using FluxRead.Interop;
using FluxRead.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Windows.Graphics;
using WinRT.Interop;

namespace FluxRead.Views;

/// <summary>
/// Live optical-capture screen. Owns the Win32-coupled setup and loop orchestration (region
/// selection, F8 calibration, live previews, running the loop) and pushes status into
/// <see cref="LiveCaptureViewModel"/>.
/// </summary>
public sealed partial class LiveCaptureView : UserControl
{
    private const int CalibrationCropWidth = 220;
    private const int CalibrationCropHeight = 90;

    private readonly Window _owner;
    private readonly DecodePipelineService _pipeline;
    private readonly DialogService _dialogs;
    private readonly ReceptionHistoryService _history;
    private readonly SettingsService _settings;
    private readonly FluxSettings _settingsModel;
    private readonly ScreenRegionCapture _previewCapture = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly Stopwatch _transferWatch = new();
    private readonly IntPtr _hwnd;

    private RectInt32 _region;
    private (int X, int Y)? _nextPoint;
    private RegionScreenCapture? _captureSource;
    private PointNextClicker? _clicker;
    private CaptureLoopService? _loop;
    private CancellationTokenSource? _cts;
    private MiniCaptureWindow? _mini;

    public LiveCaptureViewModel Vm { get; } = new();

    public LiveCaptureView(
        Window owner, DecodePipelineService pipeline, DialogService dialogs, ReceptionHistoryService history,
        SettingsService settings, FluxSettings settingsModel)
    {
        _owner = owner;
        _pipeline = pipeline;
        _dialogs = dialogs;
        _history = history;
        _settings = settings;
        _settingsModel = settingsModel;
        _hwnd = WindowNative.GetWindowHandle(owner);
        InitializeComponent();

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _previewTimer.Tick += (_, _) => RefreshPreviews();

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => UpdateTiming();

        // Recovery is user-paced (they navigate the sender), so freeze elapsed/speed/ETA on entry.
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LiveCaptureViewModel.IsRecovering) && Vm.IsRecovering)
            {
                _elapsedTimer.Stop();
                _transferWatch.Stop();
            }
        };

        // Keep the activity log scrolled to the newest line.
        Vm.Log.CollectionChanged += (_, _) =>
        {
            if (Vm.Log.Count > 0)
                LogList.ScrollIntoView(Vm.Log[^1]);
        };
    }

    /// <summary>x:Bind helper: shows an element only when its source value exists.</summary>
    public Visibility Shown(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>x:Bind helper: the inverse of a bool-to-Visibility binding.</summary>
    public Visibility Hidden(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    private async void OnDetectRegion(object sender, RoutedEventArgs e)
    {
        Vm.RegionText = "Scanning the screen for a frame…";

        var virtualScreen = DpiUtil.GetVirtualScreenPhysical();
        using var shot = await CaptureWithShellHiddenAsync(virtualScreen);

        var regions = await Task.Run(() => new FrameLocator(ColorMap.Default).Locate(shot));

        if (regions.Count == 0)
        {
            Vm.RegionText = "No frame found — select the region manually.";
            await SelectRegionManuallyAsync();
            return;
        }

        FrameRegion chosen;
        if (regions.Count == 1)
        {
            chosen = regions[0];
        }
        else
        {
            var picker = await FramePickerDialog.CreateAsync(shot, regions);
            await _dialogs.ShowAsync(picker);
            if (picker.SelectedIndex is not { } index)
            {
                Vm.RegionText = "No region selected.";
                return;
            }

            chosen = regions[index];
        }

        ApplyRegion(new RectInt32(virtualScreen.X + chosen.X, virtualScreen.Y + chosen.Y, chosen.Width, chosen.Height));

        // Reuse the same screenshot to also find the Next button in the toolbar beside the frame.
        Vm.CalibrationText = "Looking for the Next button…";
        if (!await TryAutoNextAsync(shot, virtualScreen, chosen))
            Vm.CalibrationText = "Next button not found — calibrate it with F8.";
    }

    private FrameRegion ToShotRegion(RectInt32 virtualScreen) =>
        new(_region.X - virtualScreen.X, _region.Y - virtualScreen.Y, _region.Width, _region.Height, null);

    private async Task<bool> TryAutoNextAsync(SKBitmap shot, RectInt32 virtualScreen, FrameRegion frame)
    {
        // The presenter's toolbar (carrying the "Next" button) sits above the frame; scan that band
        // first, then fall back to below in case a layout puts the controls there.
        int sx = Math.Max(0, frame.X - frame.Width / 4);
        int sw = Math.Min(shot.Width - sx, frame.Width + frame.Width / 2);
        if (sw <= 0)
            return false;

        int aboveHeight = Math.Min(frame.Y, frame.Height);
        int belowY = frame.Y + frame.Height;
        int belowHeight = Math.Min(shot.Height - belowY, frame.Height);

        return await ScanStripForNextAsync(shot, virtualScreen, sx, frame.Y - aboveHeight, sw, aboveHeight)
            || await ScanStripForNextAsync(shot, virtualScreen, sx, belowY, sw, belowHeight);
    }

    private async Task<bool> ScanStripForNextAsync(SKBitmap shot, RectInt32 virtualScreen, int sx, int sy, int sw, int sh)
    {
        if (sh <= 0)
            return false;

        using var strip = new SKBitmap(sw, sh, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(strip))
            canvas.DrawBitmap(shot, new SKRect(sx, sy, sx + sw, sy + sh), new SKRect(0, 0, sw, sh));

        return await FluxRead.Interop.OcrNextLocator.FindNextAsync(strip, virtualScreen.X + sx, virtualScreen.Y + sy) is { } point
            && ApplyNextPoint(point);
    }

    private async void OnSelectRegionManual(object sender, RoutedEventArgs e) => await SelectRegionManuallyAsync();

    private async Task SelectRegionManuallyAsync()
    {
        Minimize();
        await Task.Delay(350);
        var region = await RegionSelectOverlay.SelectAsync();
        Restore();

        if (region is { } chosen)
            ApplyRegion(chosen);
    }

    private void ApplyRegion(RectInt32 region)
    {
        _region = region;
        Vm.HasRegion = true;
        Vm.RegionText = $"Region: {region.Width}×{region.Height} at ({region.X},{region.Y})";
        StartPreview();
    }

    private void OnCalibrate(object sender, RoutedEventArgs e)
    {
        var hotkey = new HotkeyListener(_owner);
        hotkey.Pressed += (_, _) =>
        {
            FluxRead.Interop.NativeMethods.GetCursorPos(out var pos);
            ApplyNextPoint((pos.X, pos.Y));
            hotkey.Dispose();
        };
        hotkey.Arm();
        Vm.CalibrationText = "Hover over the sender's NEXT button, then press F8…";
    }

    private bool ApplyNextPoint((int X, int Y) point)
    {
        _nextPoint = point;
        if (_clicker is not null)
            _clicker.Point = point;   // retarget a running loop after a stall recalibration
        Vm.HasCalibration = true;
        Vm.CalibrationText = $"Next button at ({point.X},{point.Y})";
        StartPreview();
        return true;
    }

    private void StartPreview()
    {
        if (!Vm.IsRunning && (Vm.HasRegion || _nextPoint is not null))
        {
            RefreshPreviews();
            _previewTimer.Start();
        }
    }

    private async void RefreshPreviews()
    {
        try
        {
            if (Vm.HasRegion)
            {
                using var region = _previewCapture.Capture(_region);
                Vm.RegionPreview = await BitmapConverter.ToImageSourceAsync(region);
            }

            if (_nextPoint is { } p)
            {
                var crop = new RectInt32(
                    p.X - CalibrationCropWidth / 2, p.Y - CalibrationCropHeight / 2,
                    CalibrationCropWidth, CalibrationCropHeight);
                using var preview = _previewCapture.Capture(crop);
                Vm.CalibrationPreview = await BitmapConverter.ToImageSourceAsync(preview);
            }
        }
        catch
        {
            // Preview is best-effort; ignore transient capture errors.
        }
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_nextPoint is not { } point || !Vm.HasRegion)
            return;

        _previewTimer.Stop();
        Vm.RegionPreview = null;
        Vm.CalibrationPreview = null;

        WindowPlacement.EnsureOutsideRegion(_hwnd, _region);

        _cts = new CancellationTokenSource();
        Vm.IsRunning = true;
        Vm.IsPaused = false;
        Vm.TransferProgress = 0;
        Vm.ElapsedText = "";
        Vm.EtaText = "";
        Vm.ClearLog();
        Vm.AddLog("Starting optical transfer…");
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
            assemblerFactory: metadata => _history.OpenAssembler(ReceptionPaths.SessionRoot, metadata));
        var progress = new Progress<LoopStatus>(Vm.Apply);

        // Default: expanded on multi-monitor, collapsed on single — until the user picks and we save it.
        bool expanded = _settingsModel.MiniCaptureExpanded
            ?? FluxRead.Interop.NativeMethods.GetSystemMetrics(FluxRead.Interop.NativeMethods.SM_CMONITORS) > 1;
        _mini = new MiniCaptureWindow(Vm, _hwnd, TogglePause, () => _cts?.Cancel(), expanded, OnMiniExpandedChanged);
        if (_owner.Content is FrameworkElement shellRoot)
            _mini.ApplyTheme(shellRoot.RequestedTheme);

        // The shell is about to be hidden, and a dialog on its XamlRoot would be hidden with it.
        var shellDialogRoot = _dialogs.XamlRootSource;
        _dialogs.XamlRootSource = () => _mini?.Content?.XamlRoot ?? shellDialogRoot?.Invoke();

        _owner.AppWindow.Hide();
        _mini.Activate();

        try
        {
            var report = await Task.Run(() => _loop.RunAsync(progress, ResolveStallAsync, _cts.Token, ResolveResumeAsync));
            await HandleReportAsync(report);
        }
        catch (Exception ex)
        {
            Vm.AddLog($"Error: {ex.Message}");
            Vm.StateText = "Failed";
        }
        finally
        {
            TaskbarProgress.Current.Clear();
            _elapsedTimer.Stop();
            _transferWatch.Stop();
            Vm.IsRunning = false;
            _loop = null;
            _clicker = null;
            _captureSource = null;
            _mini?.Close();
            _mini = null;
            _dialogs.XamlRootSource = shellDialogRoot;
            _owner.AppWindow.Show();
            _owner.Activate();
        }
    }

    private void OnMiniExpandedChanged(bool expanded)
    {
        _settingsModel.MiniCaptureExpanded = expanded;
        _settings.Save(_settingsModel);
    }

    private void OnTogglePause(object sender, RoutedEventArgs e) => TogglePause();

    private void TogglePause()
    {
        if (_loop is null)
            return;

        if (_loop.IsPaused)
        {
            _loop.Resume();
            _transferWatch.Start();
            Vm.IsPaused = false;
            Vm.AddLog("Resumed.");
        }
        else
        {
            _loop.Pause();
            _transferWatch.Stop();
            Vm.IsPaused = true;
            Vm.AddLog("Paused.");
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // Ticks once a second during a transfer: elapsed wall-clock and a frames-based ETA.
    private void UpdateTiming()
    {
        var elapsed = _transferWatch.Elapsed;
        Vm.ElapsedText = "Elapsed " + FormatSpan(elapsed);

        int received = Vm.ReceivedCount, expected = Vm.ExpectedCount;
        Vm.SpeedText = received > 0 && elapsed.TotalSeconds > 0
            ? Flux.Ui.ByteFormat.Rate(Vm.ReceivedBytes / elapsed.TotalSeconds)
            : "";

        if (received > 0 && expected > 0 && received < expected)
        {
            double perFrame = elapsed.TotalSeconds / received;
            Vm.EtaText = "~" + FormatSpan(TimeSpan.FromSeconds(perFrame * (expected - received))) + " left";
        }
        else
        {
            Vm.EtaText = "";
        }
    }

    private static string FormatSpan(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes:D2}:{t.Seconds:D2}";

    private Task<StallResolution> ResolveStallAsync(CancellationToken cancellationToken) => OnUiAsync(async () =>
    {
        var dialog = new StallDialog(
            "The sender stopped advancing after several tries. Resume, re-find the Next button, "
            + "or re-detect the frame — then FluxRead keeps going.");
        await _dialogs.ShowAsync(dialog);

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
    });

    private Task<ResumeMode> ResolveResumeAsync(ResumeContext context, CancellationToken cancellationToken) => OnUiAsync(async () =>
    {
        var dialog = new ResumeDialog(context.ReceivedFrames, context.ExpectedPayloadFrames, context.FirstMissingFrameId);
        await _dialogs.ShowAsync(dialog);

        switch (dialog.Choice)
        {
            case ResumeChoice.Automatic:
                Vm.AddLog($"Resuming — skipping ahead to frame {context.FirstMissingFrameId}.");
                return ResumeMode.Automatic;

            case ResumeChoice.Manual:
                var manual = new ManualResumeDialog(context.FirstMissingFrameId);
                await _dialogs.ShowAsync(manual);
                if (manual.Continued)
                {
                    Vm.AddLog($"Resuming manually from frame {context.FirstMissingFrameId}.");
                    return ResumeMode.Manual;
                }

                _cts?.Cancel();
                return ResumeMode.Automatic;

            case ResumeChoice.StartOver:
                Vm.AddLog("Starting over — discarding received frames.");
                return ResumeMode.StartOver;

            default:
                // Cancelled: return any mode; the seek aborts on the cancelled token, keeping received frames.
                _cts?.Cancel();
                return ResumeMode.Automatic;
        }
    });

    // The loop runs on a worker thread, so every prompt it asks for has to hop back to the UI thread.
    private Task<T> OnUiAsync<T>(Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>();
        bool queued = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                completion.SetResult(await action());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        if (!queued)
            completion.SetException(new InvalidOperationException("The UI thread is no longer accepting work."));

        return completion.Task;
    }

    private async Task<SKBitmap> CaptureWithShellHiddenAsync(RectInt32 virtualScreen)
    {
        Minimize();
        await Task.Delay(350);
        var shot = _previewCapture.Capture(virtualScreen);
        Restore();
        return shot;
    }

    // During a transfer the shell is hidden and the mini window is the one in the way.
    private void Minimize() => ((_mini ?? _owner).AppWindow.Presenter as OverlappedPresenter)?.Minimize();

    private void Restore() => ((_mini ?? _owner).AppWindow.Presenter as OverlappedPresenter)?.Restore();

    private async Task RecalibrateNextAsync()
    {
        Vm.AddLog("Re-finding the Next button…");
        var virtualScreen = DpiUtil.GetVirtualScreenPhysical();
        using var shot = await CaptureWithShellHiddenAsync(virtualScreen);

        if (!await TryAutoNextAsync(shot, virtualScreen, ToShotRegion(virtualScreen)))
        {
            Vm.AddLog("Couldn't find Next automatically — use F8 to calibrate.");
            await RecalibrateWithF8Async();
        }
    }

    private async Task RecalibrateWithF8Async()
    {
        using var hotkey = new HotkeyListener(_owner);
        var captured = new TaskCompletionSource<(int X, int Y)>();
        hotkey.Pressed += (_, _) =>
        {
            FluxRead.Interop.NativeMethods.GetCursorPos(out var pos);
            captured.TrySetResult((pos.X, pos.Y));
        };
        hotkey.Arm();

        await _dialogs.InformAsync("Recalibrate", "Hover over the sender's Next button, press F8, then choose OK.");
        if (captured.Task.IsCompleted)
            ApplyNextPoint(captured.Task.Result);
    }

    private async Task AdjustRegionAsync()
    {
        Vm.AddLog("Re-detecting the frame…");
        var virtualScreen = DpiUtil.GetVirtualScreenPhysical();
        using var shot = await CaptureWithShellHiddenAsync(virtualScreen);

        var regions = await Task.Run(() => new FrameLocator(ColorMap.Default).Locate(shot));
        if (regions.Count == 0)
        {
            Vm.AddLog("No frame found — keeping the current region.");
            return;
        }

        FrameRegion chosen;
        if (regions.Count == 1)
        {
            chosen = regions[0];
        }
        else
        {
            var picker = await FramePickerDialog.CreateAsync(shot, regions);
            await _dialogs.ShowAsync(picker);
            if (picker.SelectedIndex is not { } index)
                return;
            chosen = regions[index];
        }

        _region = new RectInt32(virtualScreen.X + chosen.X, virtualScreen.Y + chosen.Y, chosen.Width, chosen.Height);
        if (_captureSource is not null)
            _captureSource.Region = _region;
        Vm.RegionText = $"Region: {_region.Width}×{_region.Height} at ({_region.X},{_region.Y})";
        Vm.AddLog("Region updated.");
    }

    private async Task HandleReportAsync(TransferReport report)
    {
        Vm.AddLog(report.Summary());

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
                ? await _dialogs.PickFolderAsync("Choose a folder to extract into")
                : await _dialogs.PickSaveFileAsync("Save decoded file", metadata.OriginalName);

            if (target is null)
            {
                Vm.AddLog("Save cancelled.");
                return;
            }

            _transferWatch.Start();

            IProgress<int>? progress = null;
            if (isArchive)
            {
                Vm.StateText = "Decompressing…";
                Vm.IsDecompressing = true;
                progress = new Progress<int>(p =>
                {
                    Vm.DecompressProgress = p / 100.0;
                    Vm.StateText = $"Decompressing… {p}%";
                });
            }

            await _pipeline.SaveAsync(report.Assembler, metadata, target, progress);

            // Verified and saved: retire the received buffer, keeping the manifest as a history record.
            if (report.Assembler.IsPersistent)
                _history.MarkComplete(Path.GetDirectoryName(report.Assembler.PayloadFilePath)!, target);

            Vm.IsDecompressing = false;
            Vm.AddLog($"Saved to {target}");
            Vm.StateText = "Saved";
            _dialogs.OpenInExplorer(target);
        }
        finally
        {
            Vm.IsDecompressing = false;
            report.Assembler.Dispose();
        }
    }
}
