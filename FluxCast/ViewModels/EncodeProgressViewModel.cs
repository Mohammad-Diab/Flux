using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flux.Ui.Controls;
using FluxCore.Transfer;
using Microsoft.Extensions.Logging;

namespace FluxCast.ViewModels;

/// <summary>
/// Progress screen: runs the encode session, showing an indeterminate bar during
/// compression and a determinate bar during frame rendering.
/// </summary>
public partial class EncodeProgressViewModel : ObservableObject
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<EncodeProgressViewModel> _logger;

    [ObservableProperty]
    private string _phaseText = "Preparing…";

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _detailText = "";

    [ObservableProperty]
    private string? _errorText;

    // Construction immediately starts the encode; callbacks run on the UI thread.
    public EncodeProgressViewModel(
        FluxEncodeService encodeService,
        string sourcePath,
        string sessionRoot,
        EncodeOptions options,
        Action<EncodeSessionResult> onCompleted,
        Action onCancelledOrFailed,
        ILogger<EncodeProgressViewModel> logger)
    {
        _logger = logger;
        _ = RunAsync(encodeService, sourcePath, sessionRoot, options, onCompleted, onCancelledOrFailed);
    }

    [RelayCommand]
    private void Cancel() => _cts.Cancel();

    [RelayCommand]
    private void AcknowledgeError() => _acknowledgeError?.Invoke();

    private Action? _acknowledgeError;

    private async Task RunAsync(
        FluxEncodeService encodeService,
        string sourcePath,
        string sessionRoot,
        EncodeOptions options,
        Action<EncodeSessionResult> onCompleted,
        Action onCancelledOrFailed)
    {
        _acknowledgeError = onCancelledOrFailed;
        var progress = new Progress<EncodeProgress>(OnProgress);

        try
        {
            var result = await Task.Run(
                () => encodeService.EncodeAsync(sourcePath, sessionRoot, options, progress, _cts.Token));
            TaskbarProgress.Current.Clear();
            onCompleted(result);
        }
        catch (OperationCanceledException)
        {
            TaskbarProgress.Current.Clear();
            onCancelledOrFailed();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Encoding failed for {Source}", sourcePath);
            TaskbarProgress.Current.Clear();
            IsIndeterminate = false;
            ErrorText = ex.Message;
        }
    }

    private void OnProgress(EncodeProgress report)
    {
        switch (report.Phase)
        {
            case EncodePhase.Compressing:
                PhaseText = "Compressing…";
                if (report.CompressionPercent >= 0)
                {
                    IsIndeterminate = false;
                    ProgressValue = report.CompressionPercent / 100.0;
                    DetailText = $"Archiving the source with 7-Zip — {report.CompressionPercent}%";
                    TaskbarProgress.Current.Report(ProgressValue);
                }
                else
                {
                    IsIndeterminate = true;
                    DetailText = "Archiving the source with 7-Zip. This can take a while for large inputs.";
                    TaskbarProgress.Current.Indeterminate();
                }
                break;

            case EncodePhase.RenderingFrames:
                PhaseText = "Rendering frames…";
                DetailText = $"{report.CompletedFrames} of {report.TotalFrames} frames";
                IsIndeterminate = false;
                ProgressValue = report.TotalFrames == 0 ? 0 : (double)report.CompletedFrames / report.TotalFrames;
                TaskbarProgress.Current.Report(ProgressValue);
                break;

            case EncodePhase.Completed:
                PhaseText = "Done";
                ProgressValue = 1;
                break;
        }
    }
}
