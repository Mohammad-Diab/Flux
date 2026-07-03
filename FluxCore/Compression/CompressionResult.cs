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

    /// <summary>
    /// Initializes a new instance of the <see cref="CompressionResult"/> class.
    /// </summary>
    /// <param name="data">Compressed data.</param>
    /// <param name="originalSize">Original uncompressed size.</param>
    public CompressionResult(byte[] data, long originalSize)
    {
    Data = data ?? throw new ArgumentNullException(nameof(data));
   OriginalSize = originalSize;
    }
}
