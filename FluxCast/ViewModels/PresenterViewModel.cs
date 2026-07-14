using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flux.Ui.Services;
using FluxCast.Services;
using FluxCore.Transfer;

namespace FluxCast.ViewModels;

/// <summary>
/// Presenter screen: shows one frame at a time with manual Next/Back navigation.
/// FluxRead confirms advancement by decoding the frame id, so frames must render
/// pixel-perfect and the window must stay put during a transfer.
/// </summary>
public partial class PresenterViewModel : ObservableObject
{
    private readonly CachedFrameProvider _frames;
    private readonly Action _onClose;
    private readonly DialogService _dialogs;

    /// <summary>Gets the total frame count, including frame 0.</summary>
    public uint TotalFrames { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(FirstCommand))]
    [NotifyCanExecuteChangedFor(nameof(LastCommand))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _currentIndex;

    [ObservableProperty]
    private ImageSource? _currentFrame;

    [ObservableProperty]
    private string _gotoText = "";

    /// <summary>Gets the progress label.</summary>
    public string ProgressText => $"Frame {CurrentIndex + 1} of {TotalFrames}";

    public PresenterViewModel(EncodeSessionResult session, Action onClose, DialogService dialogs)
    {
        _onClose = onClose;
        _dialogs = dialogs;
        TotalFrames = session.TotalFrames;
        _frames = new CachedFrameProvider(session.FramesDirectory, session.TotalFrames);

        CurrentFrame = _frames.GetFrame(0);
        GotoText = "1";
    }

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next() => CurrentIndex++;

    private bool CanNext() => CurrentIndex < TotalFrames - 1;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back() => CurrentIndex--;

    private bool CanBack() => CurrentIndex > 0;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void First() => CurrentIndex = 0;

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Last() => CurrentIndex = (int)TotalFrames - 1;

    [RelayCommand]
    private void Goto()
    {
        if (int.TryParse(GotoText, out int frame) && frame >= 1 && frame <= TotalFrames)
            CurrentIndex = frame - 1;
        else
            SyncGotoText();
    }

    [RelayCommand]
    private void Close()
    {
        if (_dialogs.Confirm(
                "End session",
                "Return to the home screen? This cast stays in Recent casts, so you can resume it later."))
            _onClose();
    }

    partial void OnCurrentIndexChanged(int value)
    {
        CurrentFrame = _frames.GetFrame(value);
        SyncGotoText();
    }

    private void SyncGotoText() => GotoText = (CurrentIndex + 1).ToString();
}
