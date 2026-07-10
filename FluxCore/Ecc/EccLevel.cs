using FluxCore.Framing;

namespace FluxCore.Ecc;

/// <summary>
/// Error-correction strength. Each frame carries 53 RS(255,k) codewords; the level sets k.
/// </summary>
public enum EccLevel : byte
{
    /// <summary>RS(255,223): 32 parity symbols, corrects 16 errors per codeword (6.3% overhead).</summary>
    Low = 0,

    /// <summary>RS(255,191): 64 parity symbols, corrects 32 errors per codeword (12.5% overhead). Default.</summary>
    Medium = 1,

    /// <summary>RS(255,159): 96 parity symbols, corrects 48 errors per codeword (18.8% overhead).</summary>
    High = 2,

    /// <summary>RS(255,127): 128 parity symbols, corrects 64 errors per codeword (25% overhead). Used for frame 0.</summary>
    Max = 3,
}

/// <summary>
/// Capacity math for <see cref="EccLevel"/> values.
/// </summary>
public static class EccLevelExtensions
{
    /// <summary>Gets k: the number of data bytes per RS(255,k) codeword.</summary>
    public static int DataBytesPerCodeword(this EccLevel level) => level switch
    {
        EccLevel.Low => 223,
        EccLevel.Medium => 191,
        EccLevel.High => 159,
        EccLevel.Max => 127,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>Gets the number of parity symbols per codeword (255 - k).</summary>
    public static int ParitySymbols(this EccLevel level) =>
        FrameFormat.CodewordLength - level.DataBytesPerCodeword();

    /// <summary>Gets the maximum number of correctable symbol errors per codeword.</summary>
    public static int CorrectableErrorsPerCodeword(this EccLevel level) => level.ParitySymbols() / 2;

    /// <summary>Gets the payload capacity of one frame at this level and the default depth (53 x k bytes).</summary>
    public static int PayloadBytesPerFrame(this EccLevel level) =>
        level.PayloadBytesPerFrame(FrameFormat.CodewordCount);

    /// <summary>Gets the payload capacity of one frame carrying <paramref name="codewordCount"/> codewords.</summary>
    /// <param name="level">ECC level determining k.</param>
    /// <param name="codewordCount">Codewords per frame (depends on the colour depth).</param>
    public static int PayloadBytesPerFrame(this EccLevel level, int codewordCount) =>
        codewordCount * level.DataBytesPerCodeword();
}
