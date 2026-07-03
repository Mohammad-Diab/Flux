namespace FluxCore.Ecc;

/// <summary>
/// Exception thrown when error correction operations fail.
/// </summary>
public class EccException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EccException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public EccException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="EccException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public EccException(string message, Exception innerException) 
        : base(message, innerException) { }
}
