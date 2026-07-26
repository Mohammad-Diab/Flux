using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flux.Ui.Controls;
using Flux.Ui.Services;
using FluxCore.Framing;
using FluxRead.Services;
using Microsoft.Extensions.Logging;

namespace FluxRead.ViewModels;

/// <summary>
/// Folder-decode screen: pick a folder of frame PNGs, decode them, show a per-frame results
/// grid, and save the reassembled (and SHA-verified) payload.
/// </summary>
public partial class FolderDecodeViewModel : ObservableObject
{
    private readonly DecodePipelineService _pipeline;
    private readonly DialogService _dialogs;
    private readonly ILogger<FolderDecodeViewModel> _logger;

    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private FolderDecodeResult? _result;
    private CancellationTokenSource? _cts;
    private PauseGate? _pause;
    private int _completed;
    private int _total;
    private int _failed;

    [ObservableProperty]
    private string? _framesFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isDecoding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isSaving;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _summary = "Choose a folder of frame images to decode.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _canSave;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _pauseLabel = "Pause";

    [ObservableProperty]
    private string _decodedCountText = "0";

    [ObservableProperty]
    private string _progressCaption = "";

    [ObservableProperty]
    private string? _failedCountText;

    [ObservableProperty]
    private string _elapsedText = "";

    [ObservableProperty]
    private string _speedText = "";

    [ObservableProperty]
    private string _etaText = "";

    /// <summary>Gets whether a decode or a save is running, so the pickers stay disabled.</summary>
    public bool IsBusy => IsDecoding || IsSaving;

    /// <summary>Gets the per-frame decode result rows.</summary>
    public ObservableCollection<FrameRow> Rows { get; } = [];

    public FolderDecodeViewModel(
        DecodePipelineService pipeline, DialogService dialogs, ILogger<FolderDecodeViewModel> logger)
    {
        _pipeline = pipeline;
        _dialogs = dialogs;
        _logger = logger;
        _ticker.Tick += (_, _) => UpdateTiming();
    }

    [RelayCommand]
    private async Task PickAndDecodeAsync()
    {
        var folder = _dialogs.PickFolder("Choose the folder of frame images");
        if (folder is null)
            return;

        FramesFolder = folder;
        await DecodeAsync(folder);
    }

    /// <summary>Holds the decode between frames, or releases it. No-op when not decoding.</summary>
    [RelayCommand]
    private void TogglePause() => SetPaused(!IsPaused);

    /// <summary>Holds or releases the decode. Idempotent; no-op when not decoding.</summary>
    /// <param name="paused">True to hold the decode at its next frame.</param>
    public void SetPaused(bool paused)
    {
        if (_pause is null || !IsDecoding || _pause.IsPaused == paused)
            return;

        if (paused)
        {
            _pause.Pause();
            _clock.Stop();
        }
        else
        {
            _pause.Resume();
            _clock.Start();
        }

        IsPaused = _pause.IsPaused;
        PauseLabel = IsPaused ? "Resume" : "Pause";
        ProgressCaption = BuildCaption();
    }

    /// <summary>Stops the decode; frames already decoded stay in the grid. No-op when not decoding.</summary>
    [RelayCommand]
    public void CancelDecode()
    {
        if (!IsDecoding)
            return;

        // A paused loop is parked inside the gate — open it so the cancellation is observed.
        _pause?.Resume();
        _cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_result?.Metadata is null)
            return;

        var metadata = _result.Metadata;
        string? target = metadata.PayloadType == PayloadType.Raw
            ? _dialogs.PickSaveFile("Save decoded file", metadata.OriginalName)
            : _dialogs.PickFolder("Choose a folder to extract into");

        if (target is null)
            return;

        try
        {
            IsSaving = true;
            ProgressValue = 0;
            StatusText = metadata.PayloadType == PayloadType.Raw ? "Saving…" : "Decompressing…";
            var progress = new Progress<int>(p =>
            {
                ProgressValue = p / 100.0;
                TaskbarProgress.Current.Report(ProgressValue);
            });
            await _pipeline.SaveAsync(_result.Assembler!, metadata, target, progress);
            StatusText = $"Saved to {target}";
            _dialogs.OpenInExplorer(target);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save failed");
            StatusText = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
            ProgressValue = 0;
            TaskbarProgress.Current.Clear();
        }
    }

    private async Task DecodeAsync(string folder)
    {
        // Release any temp payload file from a previous disk-backed decode.
        _result?.Assembler?.Dispose();
        _result = null;

        _cts = new CancellationTokenSource();
        _pause = new PauseGate();
        IsDecoding = true;
        IsPaused = false;
        PauseLabel = "Pause";
        CanSave = false;
        StatusText = null;
        Rows.Clear();
        ProgressValue = 0;
        _completed = 0;
        _failed = 0;
        _total = 0;
        Summary = "Decoding…";
        ResetReadout();
        _clock.Restart();
        _ticker.Start();

        var progress = new Progress<DecodeProgress>(OnFrameDecoded);

        try
        {
            _result = await _pipeline.DecodeFolderAsync(folder, progress, _pause, _cts.Token);
            ApplyResult(_result);
        }
        catch (OperationCanceledException)
        {
            Summary = $"Decode cancelled — {_completed} of {_total} frames processed.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Decode failed for {Folder}", folder);
            Summary = $"Decode failed: {ex.Message}";
        }
        finally
        {
            _ticker.Stop();
            _clock.Stop();
            IsDecoding = false;
            IsPaused = false;
            _pause?.Dispose();
            _pause = null;
            TaskbarProgress.Current.Clear();
        }
    }

    // Rows stream in as they decode, so the grid and the counters stay live during a long run.
    private void OnFrameDecoded(DecodeProgress p)
    {
        Rows.Add(p.Row);
        if (!p.Row.Success)
            _failed++;

        _completed = p.Completed;
        _total = p.Total;
        ProgressValue = p.Total == 0 ? 0 : (double)p.Completed / p.Total;
        TaskbarProgress.Current.Report(ProgressValue);

        DecodedCountText = $"{p.Completed} / {p.Total}";
        FailedCountText = _failed > 0 ? $"{_failed} failed" : null;
        UpdateTiming();
    }

    private void UpdateTiming()
    {
        var elapsed = _clock.Elapsed;
        ProgressCaption = BuildCaption();
        ElapsedText = "Elapsed " + FormatSpan(elapsed);

        if (_completed > 0 && elapsed.TotalSeconds > 0)
        {
            double perFrame = elapsed.TotalSeconds / _completed;
            SpeedText = perFrame > 0 ? $"{1 / perFrame:0.0} frames/s" : "";
            EtaText = _completed < _total
                ? "~" + FormatSpan(TimeSpan.FromSeconds(perFrame * (_total - _completed))) + " left"
                : "";
        }
        else
        {
            SpeedText = "";
            EtaText = "";
        }
    }

    private string BuildCaption() => IsPaused ? "frames decoded · paused" : "frames decoded";

    private void ResetReadout()
    {
        DecodedCountText = "0";
        ProgressCaption = "frames decoded";
        FailedCountText = null;
        ElapsedText = "";
        SpeedText = "";
        EtaText = "";
    }

    private static string FormatSpan(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes:D2}:{t.Seconds:D2}";

    private void ApplyResult(FolderDecodeResult result)
    {
        // A fatal frame-0 failure returns its row without ever reporting progress.
        foreach (var row in result.Rows.Skip(Rows.Count))
            Rows.Add(row);

        if (result.Error is not null)
        {
            Summary = result.Error;
            return;
        }

        int decoded = result.Rows.Count(r => r.Success);
        int failed = result.Rows.Count - decoded;
        var metadata = result.Metadata!;

        if (result.IsComplete)
        {
            Summary = $"Complete — {metadata.OriginalName} ({metadata.PayloadType}), " +
                      $"{decoded} frames decoded" + (failed > 0 ? $", {failed} failed" : "") +
                      ". Ready to save.";
            CanSave = true;
        }
        else
        {
            int missing = result.Assembler?.MissingFrameIds.Count ?? 0;
            Summary = $"Incomplete — {missing} frame(s) missing, {failed} undecodable. Cannot reassemble.";
            CanSave = false;
        }
    }
}
