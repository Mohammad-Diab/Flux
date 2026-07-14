namespace FluxCore.Imaging;

/// <summary>
/// Which family of data-tile palette a transfer uses. Carried in frame 0 so the receiver rebuilds
/// the matching palette from the count alone.
/// </summary>
public enum PaletteKind : byte
{
    /// <summary>The balanced RGB lattice — the default; higher counts trade robustness for throughput.</summary>
    Standard = 0,

    /// <summary>
    /// A chroma-hardened grayscale ladder for lossy/RDP channels: entries differ only in luma, which
    /// screen codecs preserve, so chroma loss can't merge them. Lowest throughput, highest robustness.
    /// </summary>
    Rugged = 1,
}
