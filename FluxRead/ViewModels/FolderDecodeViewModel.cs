using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    private FolderDecodeResult? _result;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string? _framesFolder;

    [ObservableProperty]
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

    /// <summary>Gets the per-frame decode result rows.</summary>
    public ObservableCollection<FrameRow> Rows { get; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderDecodeViewModel"/> class.
    /// </summary>
    public FolderDecodeViewModel(
        DecodePipelineService pipeline, DialogService dialogs, ILogger<FolderDecodeViewModel> logger)
    {
        _pipeline = pipeline;
        _dialogs = dialogs;
        _logger = logger;
    }

    [RelayCommand]
    private async Task PickAndDecodeAsync()
    {
        var folder = _dialogs.PickFramesFolder();
        if (folder is null)
            return;

        FramesFolder = folder;
        await DecodeAsync(folder);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_result?.Metadata is null)
            return;

        var metadata = _result.Metadata;
        string? target = metadata.PayloadType == PayloadType.Raw
            ? _dialogs.PickSaveFile(metadata.OriginalName)
            : _dialogs.PickOutputFolder();

        if (target is null)
            return;

        try
        {
            IsDecoding = true;
            ProgressValue = 0;
            StatusText = metadata.PayloadType == PayloadType.Raw ? "Saving…" : "Decompressing…";
            var progress = new Progress<int>(p => ProgressValue = p / 100.0);
            await _pipeline.SaveAsync(_result.Assembler!, metadata, target, progress);
            StatusText = $"Saved to {target}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save failed");
            StatusText = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsDecoding = false;
            ProgressValue = 0;
        }
    }

    private async Task DecodeAsync(string folder)
    {
        // Release any temp payload file from a previous disk-backed decode.
        _result?.Assembler?.Dispose();
        _result = null;

        _cts = new CancellationTokenSource();
        IsDecoding = true;
        CanSave = false;
        StatusText = null;
        Rows.Clear();
        ProgressValue = 0;
        Summary = "Decoding…";

        var progress = new Progress<DecodeProgress>(p =>
            ProgressValue = p.Total == 0 ? 0 : (double)p.Completed / p.Total);

        try
        {
            _result = await _pipeline.DecodeFolderAsync(folder, progress, _cts.Token);

            foreach (var row in _result.Rows)
                Rows.Add(row);

            ApplyResult(_result);
        }
        catch (OperationCanceledException)
        {
            Summary = "Decode cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Decode failed for {Folder}", folder);
            Summary = $"Decode failed: {ex.Message}";
        }
        finally
        {
            IsDecoding = false;
        }
    }

    private void ApplyResult(FolderDecodeResult result)
    {
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
