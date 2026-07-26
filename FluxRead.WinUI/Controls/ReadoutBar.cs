using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FluxRead.WinUI.Controls;

/// <summary>A progress bar tall enough to hold its readout. WinUI's ProgressBar ignores Height, so this
/// owns its track and fill instead.</summary>
public sealed class ReadoutBar : Grid
{
    private readonly Border _track = new();
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ContentPresenter _readout = new();

    public ReadoutBar()
    {
        Children.Add(_track);
        Children.Add(_fill);
        Children.Add(_readout);
        SizeChanged += (_, _) => ApplyFill();
    }

    /// <summary>Fill fraction, 0 to 1.</summary>
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(ReadoutBar),
        new PropertyMetadata(0d, (d, _) => ((ReadoutBar)d).ApplyFill()));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    /// <summary>Brush behind the fill.</summary>
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(ReadoutBar),
        new PropertyMetadata(null, (d, e) => ((ReadoutBar)d)._track.Background = e.NewValue as Brush));

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>Brush of the filled portion.</summary>
    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(Brush), typeof(ReadoutBar),
        new PropertyMetadata(null, (d, e) => ((ReadoutBar)d)._fill.Background = e.NewValue as Brush));

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <summary>Corner radius applied to both track and fill.</summary>
    public static readonly DependencyProperty BarCornerRadiusProperty = DependencyProperty.Register(
        nameof(BarCornerRadius), typeof(CornerRadius), typeof(ReadoutBar),
        new PropertyMetadata(default(CornerRadius), (d, e) =>
        {
            var bar = (ReadoutBar)d;
            bar._track.CornerRadius = (CornerRadius)e.NewValue;
            bar._fill.CornerRadius = (CornerRadius)e.NewValue;
        }));

    public CornerRadius BarCornerRadius
    {
        get => (CornerRadius)GetValue(BarCornerRadiusProperty);
        set => SetValue(BarCornerRadiusProperty, value);
    }

    /// <summary>The readout drawn over the bar.</summary>
    public static readonly DependencyProperty ReadoutProperty = DependencyProperty.Register(
        nameof(Readout), typeof(object), typeof(ReadoutBar),
        new PropertyMetadata(null, (d, e) => ((ReadoutBar)d)._readout.Content = e.NewValue));

    public object? Readout
    {
        get => GetValue(ReadoutProperty);
        set => SetValue(ReadoutProperty, value);
    }

    private void ApplyFill() => _fill.Width = Math.Max(0, ActualWidth * Math.Clamp(Fraction, 0, 1));
}
