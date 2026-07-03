namespace FluxCore.Imaging;

/// <summary>
/// Exception thrown when an invalid color operation is attempted.
/// </summary>
public class InvalidColorException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidColorException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public InvalidColorException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidColorException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public InvalidColorException(string message, Exception innerException)
        : base(message, innerException) { }
}
