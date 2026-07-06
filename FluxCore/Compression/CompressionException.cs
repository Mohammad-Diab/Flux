namespace FluxCore.Compression;

/// <summary>
/// Exception thrown when compression or decompression operations fail.
/// </summary>
public class CompressionException : Exception
{
    /// <summary>Creates the exception with an error message.</summary>
    public CompressionException(string message) : base(message) { }

    /// <summary>Creates the exception with an error message and inner exception.</summary>
    public CompressionException(string message, Exception innerException)
        : base(message, innerException) { }
}
