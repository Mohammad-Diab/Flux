namespace FluxCore.Imaging;

/// <summary>
/// Exception thrown when an invalid color operation is attempted.
/// </summary>
public class InvalidColorException : Exception
{
    /// <summary>Creates the exception with an error message.</summary>
    public InvalidColorException(string message) : base(message) { }
}
