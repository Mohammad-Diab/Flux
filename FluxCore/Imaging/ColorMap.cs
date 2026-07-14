namespace FluxCore.Imaging;

/// <summary>
/// A deterministic N-colour data-tile palette mapping tile values to colours, where N is a power of
/// two in [8, 1024]. White (255,255,255) is reserved for null/structural tiles and excluded. The
/// wire carries only the colour count (frame 0), so both ends regenerate the identical palette via
/// <see cref="PaletteGenerator"/> — no colour list crosses the channel.
/// </summary>
public sealed class ColorMap
{
    private readonly Rgb24[] _colors;

    /// <summary>Gets the default 256-colour palette.</summary>
    public static ColorMap Default { get; } = FromCount(256);

    /// <summary>Requires a supported colour count of unique colours, none of which can be white.</summary>
    public ColorMap(Rgb24[] palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        if (!PaletteGenerator.IsSupportedCount(palette.Length))
            throw new ArgumentException(
                $"Palette must be a power of two between {PaletteGenerator.MinColorCount} and {PaletteGenerator.MaxColorCount} colours, got {palette.Length}.",
                nameof(palette));

        var uniqueColors = new HashSet<Rgb24>();
        for (int i = 0; i < palette.Length; i++)
        {
            if (palette[i].IsWhite)
                throw new InvalidColorException($"Palette color at index {i} is white (255,255,255), which is reserved for null tiles.");

            if (!uniqueColors.Add(palette[i]))
                throw new InvalidColorException($"Duplicate color {palette[i]} found in palette.");
        }

        _colors = (Rgb24[])palette.Clone();
        MinimumDistance = PaletteGenerator.MinimumPairwiseDistance(_colors);
    }

    /// <summary>Gets the number of colours in the palette.</summary>
    public int Count => _colors.Length;

    /// <summary>Gets the minimum Euclidean RGB distance between any two palette colours (density measure).</summary>
    public double MinimumDistance { get; }

    /// <summary>Gets the full palette.</summary>
    public ReadOnlySpan<Rgb24> Palette => _colors;

    /// <summary>Converts a tile value to its palette color.</summary>
    public Rgb24 GetColor(int value) => _colors[value];

    /// <summary>Builds the standard-tier palette for a colour count, regenerated deterministically from the count alone.</summary>
    /// <param name="colorCount">A supported colour count (power of two in [8, 1024]).</param>
    public static ColorMap FromCount(int colorCount) => FromCount(colorCount, PaletteKind.Standard);

    /// <summary>Builds the palette for a colour count and kind, regenerated deterministically from those alone.</summary>
    /// <param name="colorCount">A supported colour count.</param>
    /// <param name="kind">Palette family (standard lattice or rugged grayscale ladder).</param>
    public static ColorMap FromCount(int colorCount, PaletteKind kind) => new(PaletteGenerator.Generate(colorCount, kind).Colors);
}
