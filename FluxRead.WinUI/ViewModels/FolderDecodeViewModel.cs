using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxCore.Framing;
using FluxRead.Services;
using Microsoft.UI.Xaml;

namespace FluxRead.WinUI.ViewModels;

/// <summary>
/// Folder-decode screen, ported from the WPF view model. The decode pipeline, rows and pause gate
/// are the same code; only the timer and the pickers are platform-specific.
/// </summary>
public partial class FolderDecodeViewModel : ObservableObject
{
    private readonly DecodePipelineService _pipeline;
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private FolderDecodeResult? _result;
    private CancellationTokenSource? _cts;
    private PauseGate? _pause;
    private int _completed;
    private int _total;
    private int _failed;

    /// <summary>Set by the view: picks a folder using the window handle WinUI pickers require.</summary>
    public Func<string, Task<string?>>? PickFolderAsync { get; set; }

    /// <summary>Set by the view: picks a save target (file for raw payloads, folder for archives).</summary>
    public Func<string, string, Task<string?>>? PickSaveFileAsync { get; set; }

    [ObservableProperty]
    private string? _framesFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isDecoding;

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
    private string _failedCountText = "";

    [ObservableProperty]
    private string _elapsedText = "";

    [ObservableProperty]
    private string _speedText = "";

    [ObservableProperty]
    private string _etaText = "";

    /// <summary>Gets whether the idle bar (summary and Save) is the one to show.</summary>
    public bool IsIdle => !IsDecoding;

    public ObservableCollection<FrameRow> Rows { get; } = [];

    public FolderDecodeViewModel(DecodePipelineService pipeline)
    {
        _pipeline = pipeline;
        _ticker.Tick += (_, _) => UpdateTiming();
    }

    [RelayCommand]
    private async Task PickAndDecodeAsync()
    {
        if (PickFolderAsync is null)
            return;

        var folder = await PickFolderAsync("Choose the folder of frame images");
        if (folder is null)
            return;

        FramesFolder = folder;
        await DecodeAsync(folder);
    }

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

    [RelayCommand]
    public void CancelDecode()
    {
        if (!IsDecoding)
            return;

        _pause?.Resume();
        _cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_result?.Metadata is null || PickSaveFileAsync is null)
            return;

        var metadata = _result.Metadata;
        bool raw = metadata.PayloadType == PayloadType.Raw;
        var target = await PickSaveFileAsync(
            raw ? "Save decoded file" : "Choose a folder to extract into", metadata.OriginalName);
        if (target is null)
            return;

        try
        {
            StatusText = raw ? "Saving…" : "Decompressing…";
            await _pipeline.SaveAsync(_result.Assembler!, metadata, target);
            StatusText = $"Saved to {target}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    private async Task DecodeAsync(string folder)
    {
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
        }
    }

    private void OnFrameDecoded(DecodeProgress p)
    {
        Rows.Add(p.Row);
        if (!p.Row.Success)
            _failed++;

        _completed = p.Completed;
        _total = p.Total;
        ProgressValue = p.Total == 0 ? 0 : (double)p.Completed / p.Total * 100;

        DecodedCountText = $"{p.Completed} / {p.Total}";
        FailedCountText = _failed > 0 ? $"{_failed} failed" : "";
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
        FailedCountText = "";
        ElapsedText = "";
        SpeedText = "";
        EtaText = "";
    }

    private static string FormatSpan(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes:D2}:{t.Seconds:D2}";

    private void ApplyResult(FolderDecodeResult result)
    {
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
