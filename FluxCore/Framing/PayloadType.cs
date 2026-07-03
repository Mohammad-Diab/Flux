namespace FluxCore.Framing;

/// <summary>
/// Type of payload in the encoding.
/// </summary>
public enum PayloadType : byte
{
    /// <summary>Raw file (uncompressed).</summary>
    Raw = 0,

    /// <summary>7z compressed archive.</summary>
    SevenZip = 1
}
