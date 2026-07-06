using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FluxCore.Decoding;
using FluxRead.Services;
using SkiaSharp;

namespace FluxRead.Views;

/// <summary>Modal chooser shown when the screen scan finds more than one frame.</summary>
public partial class FramePickerWindow : Window
{
    private sealed record Candidate(int Index, BitmapSource Thumbnail, string Label);

    public FramePickerWindow(SKBitmap screenshot, IReadOnlyList<FrameRegion> regions)
    {
        InitializeComponent();
        Flux.Ui.Controls.FluxWindowChrome.AttachCompact(this);

        var items = new List<Candidate>();
        for (int i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            using var crop = new SKBitmap();
            if (!screenshot.ExtractSubset(crop, new SKRectI(r.X, r.Y, r.X + r.Width, r.Y + r.Height)))
                continue;

            string label = r.FrameId is { } id ? $"Frame {id}" : "Frame";
            items.Add(new Candidate(i, BitmapConverter.ToBitmapSource(crop), label));
        }

        Candidates.ItemsSource = items;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) OnCancel(this, e); };
    }

    /// <summary>The chosen region index, or null when cancelled.</summary>
    public int? SelectedIndex { get; private set; }

    private void OnPick(object sender, RoutedEventArgs e)
    {
        SelectedIndex = (int)((Button)sender).Tag;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
