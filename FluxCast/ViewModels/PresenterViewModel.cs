using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>Gets the total frame count, including frame 0.</summary>
    public uint TotalFrames { get; }

    /// <summary>Gets a value indicating whether this session was fully resumed from cache.</summary>
    public bool WasResumed { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _currentIndex;

    [ObservableProperty]
    private ImageSource? _currentFrame;

    /// <summary>Gets the progress label.</summary>
    public string ProgressText => $"Frame {CurrentIndex + 1} of {TotalFrames}";

    /// <summary>
    /// Initializes a new instance of the <see cref="PresenterViewModel"/> class.
    /// </summary>
    /// <param name="session">Completed encode session.</param>
    /// <param name="onClose">Invoked when the user ends the session.</param>
    public PresenterViewModel(EncodeSessionResult session, Action onClose)
    {
        _onClose = onClose;
        TotalFrames = session.TotalFrames;
        WasResumed = session.PayloadReused && session.FramesRendered == 0;
        _frames = new CachedFrameProvider(session.FramesDirectory, session.TotalFrames);

        CurrentFrame = _frames.GetFrame(0);
    }

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next() => CurrentIndex++;

    private bool CanNext() => CurrentIndex < TotalFrames - 1;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back() => CurrentIndex--;

    private bool CanBack() => CurrentIndex > 0;

    [RelayCommand]
    private void Close() => _onClose();

    partial void OnCurrentIndexChanged(int value) => CurrentFrame = _frames.GetFrame(value);
}
