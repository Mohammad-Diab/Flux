namespace FluxCore.Imaging;

/// <summary>
/// Provides a deterministic 256-color palette mapping for byte values 0-255.
/// White (255,255,255) is reserved for null tiles and excluded from the palette.
/// </summary>
public sealed class ColorMap
{
    /// <summary>
    /// Size of a serialized color map in bytes (256 colors x 3 RGB bytes).
    /// </summary>
    public const int SerializedSize = 768;

    private readonly Rgb24[] _byteToColor;
    private readonly Dictionary<Rgb24, byte> _colorToByte;

    /// <summary>
    /// Gets the default color map with a deterministic palette.
    /// </summary>
    public static ColorMap Default { get; } = CreateDefault();

    /// <summary>Requires exactly 256 unique colors, none of which can be white.</summary>
    public ColorMap(Rgb24[] palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        if (palette.Length != 256)
            throw new ArgumentException($"Palette must contain exactly 256 colors, got {palette.Length}.", nameof(palette));

        var uniqueColors = new HashSet<Rgb24>();
        for (int i = 0; i < palette.Length; i++)
        {
            if (palette[i].IsWhite)
                throw new InvalidColorException($"Palette color at index {i} is white (255,255,255), which is reserved for null tiles.");

            if (!uniqueColors.Add(palette[i]))
                throw new InvalidColorException($"Duplicate color {palette[i]} found in palette.");
        }

        _byteToColor = (Rgb24[])palette.Clone();
        _colorToByte = new Dictionary<Rgb24, byte>(256);

        for (int i = 0; i < 256; i++)
        {
            _colorToByte[_byteToColor[i]] = (byte)i;
        }
    }

    /// <summary>Converts a tile value to its palette color.</summary>
    public Rgb24 GetColor(int value) => _byteToColor[value];

    /// <summary>Converts a palette color back to its byte value.</summary>
    public byte GetByte(Rgb24 color)
    {
        if (color.IsWhite)
            throw new InvalidColorException("White is reserved for null tiles and cannot be decoded to a byte value.");

        if (!_colorToByte.TryGetValue(color, out byte value))
            throw new InvalidColorException($"Color {color} is not in the palette.");

        return value;
    }

    /// <summary>Tries to convert a color to its byte value.</summary>
    public bool TryGetByte(Rgb24 color, out byte value)
    {
        if (color.IsWhite)
        {
            value = 0;
            return false;
        }

        return _colorToByte.TryGetValue(color, out value);
    }

    /// <summary>Checks if a color is in the palette (white is never in it).</summary>
    public bool Contains(Rgb24 color) => !color.IsWhite && _colorToByte.ContainsKey(color);

    /// <summary>Gets the full palette.</summary>
    public ReadOnlySpan<Rgb24> Palette => _byteToColor;

    /// <summary>Serializes to 256 colors × 3 RGB bytes = 768 bytes.</summary>
    public byte[] Serialize()
    {
        var buffer = new byte[SerializedSize];
        int offset = 0;

        for (int i = 0; i < 256; i++)
        {
            buffer[offset++] = _byteToColor[i].R;
            buffer[offset++] = _byteToColor[i].G;
            buffer[offset++] = _byteToColor[i].B;
        }

        return buffer;
    }

    /// <summary>Deserializes a color map from its 768-byte form.</summary>
    public static ColorMap Deserialize(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (data.Length != SerializedSize)
            throw new ArgumentException($"Serialized color map must be 768 bytes (256 colors × 3), got {data.Length}.", nameof(data));

        var palette = new Rgb24[256];
        int offset = 0;

        for (int i = 0; i < 256; i++)
        {
            byte r = data[offset++];
            byte g = data[offset++];
            byte b = data[offset++];
            palette[i] = new Rgb24(r, g, b);
        }

        return new ColorMap(palette);
    }

    // The default palette is the generator's 256-colour lattice (8 red × 8 green × 4 blue,
    // minimum pairwise distance ~36 RGB units so classification stays unambiguous under lossy
    // capture). PaletteGenerator.Generate(256) reproduces the historical palette exactly.
    private static ColorMap CreateDefault() => new(PaletteGenerator.Generate(256).Colors);
}
