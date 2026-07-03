using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxCore.Framing;

namespace FluxCast.ViewModels;

/// <summary>
/// ViewModel for the New Stream configuration dialog.
/// </summary>
public partial class NewStreamViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _selectedPath;

    [ObservableProperty]
    private bool _isFolder;

    [ObservableProperty]
    private string _inputType = "None";

    [ObservableProperty]
    private TileSize _selectedTileSize = TileSize.Size8x8;

    [ObservableProperty]
    private int _eccLevel = 1;

    [ObservableProperty]
    private bool _enableCompression = true;

    [ObservableProperty]
    private bool _exportDirectly = false;

    [ObservableProperty]
    private string? _destinationFolder;

    [ObservableProperty]
    private bool _autoPlay = false;

    [ObservableProperty]
    private double _framePeriod = 1.0;

    public bool CanConfigureCompression => !IsFolder;

    public NewStreamViewModel()
    {
    }

    partial void OnIsFolderChanged(bool value)
    {
        if (value)
        {
            EnableCompression = true;
        }
        InputType = value ? "Folder" : "File";
        OnPropertyChanged(nameof(CanConfigureCompression));
    }

    partial void OnAutoPlayChanged(bool value)
    {
        OnPropertyChanged(nameof(FramePeriod));
    }

    public bool Validate(out string errorMessage)
    {
        if (string.IsNullOrEmpty(SelectedPath))
        {
            errorMessage = "Please select a file or folder to encode.";
            return false;
        }

        if (string.IsNullOrEmpty(DestinationFolder))
        {
            errorMessage = "Please select a destination folder.";
            return false;
        }

        if (EccLevel < 1 || EccLevel > 8)
        {
            errorMessage = "ECC level must be between 1 and 8.";
            return false;
        }

        if (FramePeriod < 0.1 || FramePeriod > 10)
        {
            errorMessage = "Frame period must be between 0.1 and 10 seconds.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
