using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Flux.Ui.Views;

/// <summary>Gradient-square brand mark with an app-specific arrow glyph.</summary>
public partial class BrandMark : UserControl
{
    // Default: the downward "receive" arrow (FluxRead); FluxCast passes its "send" arrow.
    public static readonly Geometry DefaultGlyph = Geometry.Parse("M7,13 L13,7 L9.5,7 L9.5,1 L4.5,1 L4.5,7 L1,7 Z");

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(Geometry), typeof(BrandMark), new PropertyMetadata(DefaultGlyph));

    public BrandMark() => InitializeComponent();

    public Geometry Glyph
    {
        get => (Geometry)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}
