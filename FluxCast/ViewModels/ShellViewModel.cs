using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FluxCast.Services;
using FluxCore.Transfer;
using Microsoft.Extensions.Logging;

namespace FluxCast.ViewModels;

/// <summary>
/// Owns navigation between the setup, progress, and presenter screens.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly FluxEncodeService _encodeService;
    private readonly SourceValidator _validator;
    private readonly DialogService _dialogs;
    private readonly ILoggerFactory _loggerFactory;

    [ObservableProperty]
    private object? _current;

    /// <summary>Gets the root directory for encode sessions.</summary>
    public static string SessionRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Flux", "FluxCast", "sessions");

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellViewModel"/> class.
    /// </summary>
    public ShellViewModel(
        FluxEncodeService encodeService,
        SourceValidator validator,
        DialogService dialogs,
        ILoggerFactory loggerFactory)
    {
        _encodeService = encodeService;
        _validator = validator;
        _dialogs = dialogs;
        _loggerFactory = loggerFactory;

        ShowSetup();
    }

    /// <summary>Navigates to the setup screen.</summary>
    public void ShowSetup() =>
        Current = new EncodeSetupViewModel(_validator, _dialogs, StartEncode);

    private void StartEncode(string sourcePath, EncodeOptions options) =>
        Current = new EncodeProgressViewModel(
            _encodeService, sourcePath, SessionRoot, options,
            onCompleted: ShowPresenter,
            onCancelledOrFailed: ShowSetup,
            _loggerFactory.CreateLogger<EncodeProgressViewModel>());

    private void ShowPresenter(EncodeSessionResult session) =>
        Current = new PresenterViewModel(session, ShowSetup);
}
