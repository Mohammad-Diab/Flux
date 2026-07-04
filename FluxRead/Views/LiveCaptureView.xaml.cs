using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using FluxCore.Imaging;
using FluxCore.Transfer;
using FluxRead.Interop;
using FluxRead.Services;
using FluxRead.ViewModels;
using Microsoft.Win32;

namespace FluxRead.Views;

/// <summary>
/// Live optical-capture screen. Owns the Win32-coupled setup and loop orchestration (region
/// selection, F8 calibration, window relocation, running the capture loop, stall dialog, save)
/// and pushes status into <see cref="LiveCaptureViewModel"/> for display.
/// </summary>
public partial class LiveCaptureView : UserControl
{
    private readonly LiveCaptureViewModel _vm;
    private readonly DecodePipelineService _pipeline;
    private readonly DialogService _dialogs;

    private Int32Rect _region;
    private PointNextClicker? _clicker;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveCaptureView"/> class.
    /// </summary>
    /// <param name="pipeline">Shared decode/save pipeline.</param>
    /// <param name="dialogs">Dialog service.</param>
    public LiveCaptureView(DecodePipelineService pipeline, DialogService dialogs)
    {
        _pipeline = pipeline;
        _dialogs = dialogs;
        _vm = new LiveCaptureViewModel();
        DataContext = _vm;
        InitializeComponent();
    }

    private void OnSelectRegion(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var selector = new RegionSelectorWindow { Owner = owner };
        if (owner is not null)
            owner.WindowState = WindowState.Minimized;

        var confirmed = selector.ShowDialog() == true && selector.Region is { } region;
        if (owner is not null)
            owner.WindowState = WindowState.Normal;

        if (!confirmed)
            return;

        _region = selector.Region!.Value;
        _vm.HasRegion = true;
        _vm.RegionText = $"Region: {_region.Width}×{_region.Height} at ({_region.X},{_region.Y})";
    }

    private void OnCalibrate(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (owner is null)
            return;

        using var hotkey = new HotkeyListener(owner);
        var tcs = new TaskCompletionSource<(int X, int Y)>();
        hotkey.Pressed += (_, _) =>
        {
            NativeMethods.GetCursorPos(out var pos);
            tcs.TrySetResult((pos.X, pos.Y));
        };
        hotkey.Arm();

        _vm.CalibrationText = "Hover over the Client's NEXT button, then press F8…";
        CaptureNextPointAsync(tcs);
    }

    private async void CaptureNextPointAsync(TaskCompletionSource<(int X, int Y)> tcs)
    {
        var point = await tcs.Task;
        _clicker = new PointNextClicker(point);
        _vm.HasCalibration = true;
        _vm.CalibrationText = $"Next button at ({point.X},{point.Y})";
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_clicker is null || !_vm.HasRegion)
            return;

        var owner = Window.GetWindow(this);
        if (owner is not null)
        {
            var handle = new WindowInteropHelper(owner).Handle;
            WindowPlacement.EnsureOutsideRegion(handle, _region);
        }

        _cts = new CancellationTokenSource();
        _vm.IsRunning = true;
        _vm.Log.Clear();
        _vm.AddLog("Starting optical transfer…");

        var capture = new RegionScreenCapture(_region);
        var loop = new CaptureLoopService(capture, _clicker, ColorMap.Default);
        var progress = new Progress<LoopStatus>(_vm.Apply);

        try
        {
            var report = await Task.Run(() => loop.RunAsync(progress, ResolveStallAsync, _cts.Token));
            await HandleReportAsync(report);
        }
        catch (Exception ex)
        {
            _vm.AddLog($"Error: {ex.Message}");
            _vm.StateText = "Failed";
        }
        finally
        {
            _vm.IsRunning = false;
        }
    }

    private void OnStop(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private Task<StallResolution> ResolveStallAsync(CancellationToken cancellationToken) =>
        Dispatcher.InvokeAsync(() =>
        {
            var result = MessageBox.Show(
                Window.GetWindow(this)!,
                "The frame is stuck. Yes = retry the click · No = recalibrate the Next button · Cancel = abort.",
                "Transfer stalled",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    return StallResolution.Retry;
                case MessageBoxResult.No:
                    RecalibrateSynchronously();
                    return StallResolution.Recalibrate;
                default:
                    return StallResolution.Abort;
            }
        }).Task;

    private void RecalibrateSynchronously()
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

        MessageBox.Show(owner, "Hover over the Client's NEXT button and press F8, then click OK.", "Recalibrate");
        if (tcs.Task.IsCompleted && _clicker is not null)
        {
            _clicker.Point = tcs.Task.Result;
            _vm.CalibrationText = $"Next button at ({_clicker.Point.X},{_clicker.Point.Y})";
        }
    }

    private async Task HandleReportAsync(TransferReport report)
    {
        _vm.AddLog(report.Summary());

        if (report.State != CaptureLoopState.Complete || report.Assembler is null || report.Metadata is null)
            return;

        _vm.AddLog("Transfer verified. Choose where to save.");
        var metadata = report.Metadata;
        string? target = metadata.PayloadType == FluxCore.Framing.PayloadType.Raw
            ? _dialogs.PickSaveFile(metadata.OriginalName)
            : _dialogs.PickOutputFolder();

        if (target is null)
        {
            _vm.AddLog("Save cancelled.");
            return;
        }

        await _pipeline.SaveAsync(report.Assembler, metadata, target);
        _vm.AddLog($"Saved to {target}");
        _vm.StateText = "Saved";
    }
}
