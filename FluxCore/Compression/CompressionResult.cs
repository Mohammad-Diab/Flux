namespace FluxCore.Compression;

/// <summary>
/// Compression result containing the compressed data and metadata.
/// </summary>
public sealed class CompressionResult
{
    /// <summary>
    /// Gets the compressed data.
    /// </summary>
    public byte[] Data { get; init; }

    /// <summary>
    /// Gets the original uncompressed size in bytes.
    /// </summary>
    public long OriginalSize { get; init; }

    /// <summary>
    /// Gets the compressed size in bytes.
    /// </summary>
    public long CompressedSize => Data.Length;

    /// <summary>
    /// Gets the compression ratio (compressed size / original size).
    /// </summary>
    public double CompressionRatio => OriginalSize > 0 ? (double)CompressedSize / OriginalSize : 0;

    /// <summary>Creates a result from compressed data and the original size.</summary>
    public CompressionResult(byte[] data, long originalSize)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        OriginalSize = originalSize;
    }
}
