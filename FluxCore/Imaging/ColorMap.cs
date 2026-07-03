using System.Buffers.Binary;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorMap"/> class.
    /// </summary>
    /// <param name="palette">Array of exactly 256 unique colors, none of which can be white.</param>
    /// <exception cref="ArgumentNullException">Thrown when palette is null.</exception>
    /// <exception cref="ArgumentException">Thrown when palette is invalid.</exception>
    public ColorMap(Rgb24[] palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        if (palette.Length != 256)
            throw new ArgumentException($"Palette must contain exactly 256 colors, got {palette.Length}.", nameof(palette));

        // Validate no white and no duplicates
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

    /// <summary>
    /// Converts a byte value to its corresponding color.
    /// </summary>
    /// <param name="value">Byte value (0-255).</param>
    /// <returns>The corresponding RGB color.</returns>
    public Rgb24 GetColor(byte value) => _byteToColor[value];

    /// <summary>
    /// Converts a color to its corresponding byte value.
    /// </summary>
    /// <param name="color">RGB color.</param>
    /// <returns>The corresponding byte value (0-255).</returns>
    /// <exception cref="InvalidColorException">Thrown when color is not in the palette.</exception>
    public byte GetByte(Rgb24 color)
    {
        if (color.IsWhite)
            throw new InvalidColorException("White is reserved for null tiles and cannot be decoded to a byte value.");

        if (!_colorToByte.TryGetValue(color, out byte value))
            throw new InvalidColorException($"Color {color} is not in the palette.");

        return value;
    }

    /// <summary>
    /// Tries to convert a color to its corresponding byte value.
    /// </summary>
    /// <param name="color">RGB color.</param>
    /// <param name="value">The corresponding byte value if found.</param>
    /// <returns>True if color is in palette; false otherwise.</returns>
    public bool TryGetByte(Rgb24 color, out byte value)
    {
        if (color.IsWhite)
        {
            value = 0;
            return false;
        }

        return _colorToByte.TryGetValue(color, out value);
    }

    /// <summary>
    /// Checks if a color is in the palette (excluding white).
    /// </summary>
    public bool Contains(Rgb24 color) => !color.IsWhite && _colorToByte.ContainsKey(color);

    /// <summary>
    /// Gets the full palette as a read-only span.
    /// </summary>
    public ReadOnlySpan<Rgb24> Palette => _byteToColor;

    /// <summary>
    /// Serializes the color map to a byte array (256 colors � 3 bytes RGB = 768 bytes).
    /// </summary>
    /// <returns>Byte array containing the serialized palette.</returns>
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

    /// <summary>
    /// Deserializes a color map from a byte array.
    /// </summary>
    /// <param name="data">Byte array containing serialized palette (must be exactly 768 bytes).</param>
    /// <returns>Deserialized ColorMap instance.</returns>
    /// <exception cref="ArgumentException">Thrown when data is invalid.</exception>
    public static ColorMap Deserialize(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (data.Length != SerializedSize)
            throw new ArgumentException($"Serialized color map must be 768 bytes (256 colors � 3), got {data.Length}.", nameof(data));

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

    /// <summary>
    /// Creates the default deterministic 256-color palette.
    /// Uses a spread across RGB space, avoiding white (255,255,255).
    /// </summary>
    // The default palette is an evenly spaced 8x8x4 RGB lattice (8 red x 8 green x 4 blue
    // levels = 256). Minimum pairwise distance is ~36 RGB units, so classification stays
    // unambiguous under lossy capture noise. The lattice point that would be white (index 255)
    // is replaced with a lattice-gap color, keeping white reserved for structural tiles.
    private static ColorMap CreateDefault()
    {
        ReadOnlySpan<byte> redGreenLevels = [0, 36, 73, 109, 146, 182, 219, 255];
        ReadOnlySpan<byte> blueLevels = [0, 85, 170, 255];
        var palette = new Rgb24[256];

        for (int r = 0; r < 8; r++)
        {
            for (int g = 0; g < 8; g++)
            {
                for (int b = 0; b < 4; b++)
                {
                    palette[(r * 8 + g) * 4 + b] = new Rgb24(redGreenLevels[r], redGreenLevels[g], blueLevels[b]);
                }
            }
        }

        palette[255] = new Rgb24(18, 18, 43);

        return new ColorMap(palette);
    }
}
