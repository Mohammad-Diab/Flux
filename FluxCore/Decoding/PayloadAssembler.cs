using FluxCore.Compression;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;

namespace FluxCore.Decoding;

/// <summary>
/// Accumulates decoded frame payloads (in any order, tolerating duplicates), tracks
/// completeness against the metadata's declared frame count, and reassembles the transfer
/// payload with end-to-end SHA-256 verification before extraction.
/// </summary>
public sealed class PayloadAssembler
{
    private readonly MetadataPayload _metadata;
    private readonly Dictionary<uint, byte[]> _frames = new();
    private readonly int _bytesPerFrame;

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadAssembler"/> class.
    /// </summary>
    /// <param name="metadata">Decoded frame-0 metadata for the transfer.</param>
    public PayloadAssembler(MetadataPayload metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!metadata.MatchesFrameFormat())
            throw new ArgumentException("Metadata does not match this frame format version.", nameof(metadata));

        _metadata = metadata;
        _bytesPerFrame = metadata.EccLevel.PayloadBytesPerFrame();
    }

    /// <summary>Gets the number of payload frames expected (total frames minus frame 0).</summary>
    public uint ExpectedPayloadFrames => _metadata.TotalFrames - 1;

    /// <summary>Gets the number of distinct payload frames received so far.</summary>
    public int ReceivedFrames => _frames.Count;

    /// <summary>Gets a value indicating whether every payload frame has been received.</summary>
    public bool IsComplete => _frames.Count == ExpectedPayloadFrames;

    /// <summary>Gets the frame ids not yet received, in ascending order.</summary>
    public IReadOnlyList<uint> MissingFrameIds
    {
        get
        {
            var missing = new List<uint>();
            for (uint id = 1; id <= ExpectedPayloadFrames; id++)
            {
                if (!_frames.ContainsKey(id))
                    missing.Add(id);
            }

            return missing;
        }
    }

    /// <summary>
    /// Adds a decoded frame. Duplicates are ignored; inconsistent frames are rejected.
    /// </summary>
    /// <param name="header">Decoded frame header.</param>
    /// <param name="payload">Decoded frame payload (exactly PayloadLength bytes).</param>
    /// <returns>True if the frame was new; false if it was already received.</returns>
    /// <exception cref="ArgumentException">Thrown when the frame is inconsistent with the transfer metadata.</exception>
    public bool AddFrame(in FrameHeader header, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (header.TotalFrames != _metadata.TotalFrames)
            throw new ArgumentException(
                $"Frame declares {header.TotalFrames} total frames; transfer expects {_metadata.TotalFrames}.");
        if (header.IsMetadataFrame || header.FrameId == 0 || header.FrameId >= _metadata.TotalFrames)
            throw new ArgumentException($"Frame id {header.FrameId} is not a payload frame of this transfer.");

        int expectedLength = ExpectedFrameLength(header.FrameId);
        if (payload.Length != expectedLength)
            throw new ArgumentException(
                $"Frame {header.FrameId} carries {payload.Length} bytes; expected {expectedLength}.");

        return _frames.TryAdd(header.FrameId, payload);
    }

    /// <summary>
    /// Concatenates all frames in order and verifies the SHA-256 against the metadata.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when frames are missing.</exception>
    /// <exception cref="InvalidDataException">Thrown when the reassembled payload fails hash verification.</exception>
    public byte[] AssembleAndVerify()
    {
        if (!IsComplete)
            throw new InvalidOperationException(
                $"Transfer is incomplete: {MissingFrameIds.Count} of {ExpectedPayloadFrames} frames missing.");

        var payload = new byte[_metadata.PayloadLength];
        int offset = 0;
        for (uint id = 1; id <= ExpectedPayloadFrames; id++)
        {
            _frames[id].CopyTo(payload.AsSpan(offset));
            offset += _frames[id].Length;
        }

        if (!Sha256Helper.Verify(payload, _metadata.Sha256))
            throw new InvalidDataException(
                "Reassembled payload failed SHA-256 verification; one or more frames are corrupt.");

        return payload;
    }

    /// <summary>
    /// Reassembles, verifies, and extracts the transfer to a directory: 7z payloads are
    /// decompressed, raw payloads are written under their original file name.
    /// </summary>
    /// <param name="targetDirectory">Directory to extract into.</param>
    /// <param name="compression">Compression service for 7z payloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path of the extracted file, or the target directory for archives.</returns>
    public async Task<string> ExtractAsync(
        string targetDirectory, CompressionService compression, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetDirectory);
        ArgumentNullException.ThrowIfNull(compression);

        var payload = AssembleAndVerify();
        Directory.CreateDirectory(targetDirectory);

        if (_metadata.PayloadType == PayloadType.Raw)
        {
            var fileName = Path.GetFileName(_metadata.OriginalName);
            if (string.IsNullOrEmpty(fileName))
                fileName = "flux-payload.bin";

            var filePath = Path.Combine(targetDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, payload, cancellationToken);
            return filePath;
        }

        await compression.DecompressAsync(payload, targetDirectory, cancellationToken);
        return targetDirectory;
    }

    private int ExpectedFrameLength(uint frameId)
    {
        long remaining = _metadata.PayloadLength - (long)(frameId - 1) * _bytesPerFrame;
        return (int)Math.Clamp(remaining, 0, _bytesPerFrame);
    }
}
