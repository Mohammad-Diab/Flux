namespace FluxCore.Framing;

/// <summary>
/// Role of a tile within the fixed 160x90 frame grid.
/// </summary>
public enum TileRole : byte
{
    /// <summary>Part of a corner finder block (7x7 finder pattern plus its white quiet ring).</summary>
    Finder = 0,

    /// <summary>Part of the alternating black/white timing pattern (top row and left column).</summary>
    Timing = 1,

    /// <summary>Carries one symbol of a redundant frame header copy.</summary>
    Header = 2,

    /// <summary>Part of the 4x4 beacon block that flips black/white with frame id parity.</summary>
    Beacon = 3,

    /// <summary>Carries one interleaved Reed-Solomon codeword symbol.</summary>
    Data = 4,

    /// <summary>Unused trailing tile, always rendered white.</summary>
    Pad = 5,
}
