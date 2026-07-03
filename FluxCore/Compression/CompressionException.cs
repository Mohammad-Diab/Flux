namespace FluxCore.Compression;

/// <summary>
/// Exception thrown when compression or decompression operations fail.
/// </summary>
public class CompressionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompressionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public CompressionException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompressionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public CompressionException(string message, Exception innerException) 
        : base(message, innerException) { }
}
