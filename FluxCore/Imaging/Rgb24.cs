namespace FluxCore.Imaging;

/// <summary>
/// Represents a 24-bit RGB color.
/// </summary>
public readonly struct Rgb24 : IEquatable<Rgb24>
{
    /// <summary>
    /// Gets the red component (0-255).
    /// </summary>
    public byte R { get; init; }

    /// <summary>
    /// Gets the green component (0-255).
    /// </summary>
    public byte G { get; init; }

    /// <summary>
    /// Gets the blue component (0-255).
    /// </summary>
    public byte B { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rgb24"/> struct.
    /// </summary>
    /// <param name="r">Red component (0-255).</param>
    /// <param name="g">Green component (0-255).</param>
    /// <param name="b">Blue component (0-255).</param>
    public Rgb24(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <summary>
    /// White color (reserved for null tiles, must not be used for data).
    /// </summary>
    public static readonly Rgb24 White = new(255, 255, 255);

    /// <summary>
    /// Checks if this color is white (reserved).
    /// </summary>
    public bool IsWhite => R == 255 && G == 255 && B == 255;

    /// <summary>
    /// Determines whether the specified <see cref="Rgb24"/> is equal to the current <see cref="Rgb24"/>.
    /// </summary>
    public bool Equals(Rgb24 other) => R == other.R && G == other.G && B == other.B;

    /// <summary>
    /// Determines whether the specified object is equal to the current <see cref="Rgb24"/>.
    /// </summary>
    public override bool Equals(object? obj) => obj is Rgb24 other && Equals(other);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(R, G, B);

    /// <summary>
    /// Determines whether two specified <see cref="Rgb24"/> instances are equal.
    /// </summary>
    public static bool operator ==(Rgb24 left, Rgb24 right) => left.Equals(right);

    /// <summary>
    /// Determines whether two specified <see cref="Rgb24"/> instances are not equal.
    /// </summary>
    public static bool operator !=(Rgb24 left, Rgb24 right) => !left.Equals(right);

    /// <summary>
    /// Returns a string representation of the color.
    /// </summary>
    public override string ToString() => $"RGB({R}, {G}, {B})";
}
