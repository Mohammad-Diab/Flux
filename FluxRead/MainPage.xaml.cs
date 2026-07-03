using FluxRead.ViewModels;
using FluxRead.Models;

namespace FluxRead;

public partial class MainPage : ContentPage, IQueryAttributable
{
    private readonly MainViewModel _viewModel;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("InputMode", out var modeObj) && modeObj is InputMode mode)
        {
            if (mode == InputMode.Folder)
            {
                if (query.TryGetValue("SourceFolder", out var sourceObj) &&
                    query.TryGetValue("OutputFolder", out var outputObj))
                {
                    var sourceFolder = sourceObj as string ?? string.Empty;
                    var outputFolder = outputObj as string ?? string.Empty;

                    await _viewModel.StartFolderDecodeAsync(sourceFolder, outputFolder);
                }
            }
            else if (mode == InputMode.ScreenCapture)
            {
                if (query.TryGetValue("FramePeriod", out var periodObj) &&
                    query.TryGetValue("OutputFolder", out var outputObj))
                {
                    var framePeriod = (double)periodObj;
                    var outputFolder = outputObj as string ?? string.Empty;

                    await _viewModel.StartScreenCaptureAsync(framePeriod, outputFolder);
                }
            }
        }
        else if (query.TryGetValue("ResumeProgressFile", out var progressFileObj))
        {
            var progressFile = progressFileObj as string ?? string.Empty;
            await _viewModel.ResumeSessionAsync(progressFile);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Stop capture if active
        if (_viewModel.IsCapturing)
        {
            _viewModel.StopScreenCaptureCommand.Execute(null);
        }
    }
}
