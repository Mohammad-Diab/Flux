using FluxCore.Decoding;
using FluxRead.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;

namespace FluxRead.WinUI.Views;

/// <summary>Modal chooser shown when the screen scan finds more than one frame.</summary>
public sealed partial class FramePickerDialog : FluxDialog
{
    private sealed record Candidate(int Index, ImageSource Thumbnail, string Label);

    private FramePickerDialog() => InitializeComponent();

    /// <summary>Builds the chooser; async because WinUI decodes image sources off a stream.</summary>
    public static async Task<FramePickerDialog> CreateAsync(SKBitmap screenshot, IReadOnlyList<FrameRegion> regions)
    {
        var dialog = new FramePickerDialog();
        var items = new List<Candidate>();

        for (int i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            using var crop = new SKBitmap();
            if (!screenshot.ExtractSubset(crop, new SKRectI(r.X, r.Y, r.X + r.Width, r.Y + r.Height)))
                continue;

            string label = r.FrameId is { } id ? $"Frame {id}" : "Frame";
            items.Add(new Candidate(i, await BitmapConverter.ToImageSourceAsync(crop), label));
        }

        dialog.Candidates.ItemsSource = items;
        return dialog;
    }

    /// <summary>The chosen region index, or null when cancelled.</summary>
    public int? SelectedIndex { get; private set; }

    private void OnPick(object sender, RoutedEventArgs e)
    {
        SelectedIndex = (int)((Button)sender).Tag;
        Hide();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Hide();
}
