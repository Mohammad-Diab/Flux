using System.Buffers.Binary;
using FluxCore.Compression;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;

namespace FluxCore.Decoding;

/// <summary>
/// Accumulates decoded frame payloads (in any order, tolerating duplicates), tracks
/// completeness against the metadata's declared frame count, and reassembles the transfer
/// payload with end-to-end SHA-256 verification before extraction.
/// <para>
/// Small transfers are held entirely in memory. Large ones (payload ≥ <see cref="DiskThresholdBytes"/>)
/// switch to a disk-backed mode: each frame is written straight to its offset in a single
/// pre-sized temp file, so peak memory stays at roughly one frame regardless of payload size and
/// the 2 GB single-array limit no longer applies. Dispose the assembler to delete the temp file.
/// </para>
/// <para>
/// The persisting constructor is a third mode: always disk-backed, into a caller-owned directory
/// that survives <see cref="Dispose"/>, with each accepted frame id appended to a small index
/// file. This lets an interrupted reception be reopened and resumed from the frames it already
/// holds.
/// </para>
/// </summary>
public sealed class PayloadAssembler : IDisposable
{
    /// <summary>Payload size at or above which the assembler spills frames to a temp file.</summary>
    public const long DiskThresholdBytes = 100L * 1024 * 1024;

    private readonly MetadataPayload _metadata;
    private readonly int _bytesPerFrame;
    private readonly bool _useDisk;

    // In-memory mode.
    private readonly Dictionary<uint, byte[]>? _frames;

    // Disk-backed mode.
    private readonly HashSet<uint>? _diskFrameIds;
    private readonly string? _workDirectory;
    private readonly string? _payloadFilePath;
    private FileStream? _payloadStream;
    private readonly object _diskLock = new();
    private bool _writeClosed;
    private bool _disposed;

    // Persisting mode: a caller-owned session directory that survives Dispose, plus an append-only
    // index of received frame ids so the reception can be reopened and resumed.
    private readonly bool _persistent;
    private readonly string? _indexPath;
    private FileStream? _indexStream;

    /// <summary>Creates an assembler for the transfer described by the frame-0 metadata.</summary>
    public PayloadAssembler(MetadataPayload metadata)
        : this(metadata, DiskThresholdBytes)
    {
    }

    /// <summary>The explicit disk threshold exists to test the disk-backed path without a 100 MB payload.</summary>
    public PayloadAssembler(MetadataPayload metadata, long diskThresholdBytes)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!metadata.TryBuildLayout(out var layout))
            throw new ArgumentException("Metadata describes an undecodable frame format.", nameof(metadata));

        _metadata = metadata;
        _bytesPerFrame = metadata.EccLevel.PayloadBytesPerFrame(layout.CodewordsForBits(metadata.BitsPerTile));
        _useDisk = metadata.PayloadLength >= diskThresholdBytes;

        if (_useDisk)
        {
            _diskFrameIds = new HashSet<uint>();
            _workDirectory = Path.Combine(Path.GetTempPath(), "Flux", "assembly", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workDirectory);
            _payloadFilePath = Path.Combine(_workDirectory, "payload.bin");
            _payloadStream = new FileStream(
                _payloadFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            _payloadStream.SetLength(metadata.PayloadLength);
        }
        else
        {
            _frames = new Dictionary<uint, byte[]>();
        }
    }

    /// <summary>
    /// Creates a persisting, always-disk-backed assembler that writes payload bytes to
    /// <paramref name="payloadFilePath"/> and appends each accepted frame id to
    /// <paramref name="receivedIndexPath"/>, so an interrupted transfer can be resumed. Unlike the
    /// temp-file mode the files are caller-owned and are NOT deleted on <see cref="Dispose"/>.
    /// Pass <paramref name="resume"/> true to reopen an existing session and preload the frames it
    /// already holds; the caller must have verified the on-disk session is compatible with this
    /// transfer (same signature, hash, length, frame count, and ECC level).
    /// </summary>
    public PayloadAssembler(MetadataPayload metadata, string payloadFilePath, string receivedIndexPath, bool resume)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(payloadFilePath);
        ArgumentNullException.ThrowIfNull(receivedIndexPath);
        if (!metadata.TryBuildLayout(out var layout))
            throw new ArgumentException("Metadata describes an undecodable frame format.", nameof(metadata));

        _metadata = metadata;
        _bytesPerFrame = metadata.EccLevel.PayloadBytesPerFrame(layout.CodewordsForBits(metadata.BitsPerTile));
        _useDisk = true;
        _persistent = true;
        _diskFrameIds = new HashSet<uint>();
        _payloadFilePath = payloadFilePath;
        _indexPath = receivedIndexPath;

        Directory.CreateDirectory(Path.GetDirectoryName(payloadFilePath)!);
        _payloadStream = new FileStream(payloadFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        _payloadStream.SetLength(metadata.PayloadLength);

        if (resume)
        {
            PreloadReceivedIds();
            _indexStream = new FileStream(receivedIndexPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            _indexStream.Seek(0, SeekOrigin.End);
        }
        else
        {
            _indexStream = new FileStream(receivedIndexPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        }
    }

    /// <summary>Gets a value indicating whether frames are being spilled to a temp file.</summary>
    public bool IsDiskBacked => _useDisk;

    /// <summary>Gets a value indicating whether frames are persisted to a resumable session directory.</summary>
    public bool IsPersistent => _persistent;

    /// <summary>
    /// Gets the path of the assembled payload temp file (disk-backed mode only). Valid once the
    /// transfer is complete and <see cref="Verify"/> has passed.
    /// </summary>
    public string PayloadFilePath =>
        _payloadFilePath ?? throw new InvalidOperationException("This assembler is not disk-backed.");

    /// <summary>Gets the number of payload frames expected (total frames minus frame 0).</summary>
    public uint ExpectedPayloadFrames => _metadata.TotalFrames - 1;

    /// <summary>Gets the number of distinct payload frames received so far.</summary>
    public int ReceivedFrames => _useDisk ? _diskFrameIds!.Count : _frames!.Count;

    /// <summary>Gets a value indicating whether every payload frame has been received.</summary>
    public bool IsComplete => ReceivedFrames == ExpectedPayloadFrames;

    /// <summary>Gets the highest payload frame id accepted so far (0 if none).</summary>
    public uint LastAcceptedId { get; private set; }

    /// <summary>Gets the payload bytes received so far.</summary>
    public long ReceivedBytes { get; private set; }

    /// <summary>Determines whether a payload frame with the given id has been received.</summary>
    /// <param name="frameId">Payload frame id.</param>
    public bool HasFrame(uint frameId) =>
        _useDisk ? _diskFrameIds!.Contains(frameId) : _frames!.ContainsKey(frameId);

    /// <summary>Gets the frame ids not yet received, in ascending order.</summary>
    public IReadOnlyList<uint> MissingFrameIds
    {
        get
        {
            var missing = new List<uint>();
            for (uint id = 1; id <= ExpectedPayloadFrames; id++)
            {
                if (!HasFrame(id))
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (header.TotalFrames != _metadata.TotalFrames)
            throw new ArgumentException(
                $"Frame declares {header.TotalFrames} total frames; transfer expects {_metadata.TotalFrames}.");
        if (header.IsMetadataFrame || header.FrameId == 0 || header.FrameId >= _metadata.TotalFrames)
            throw new ArgumentException($"Frame id {header.FrameId} is not a payload frame of this transfer.");

        int expectedLength = ExpectedFrameLength(header.FrameId);
        if (payload.Length != expectedLength)
            throw new ArgumentException(
                $"Frame {header.FrameId} carries {payload.Length} bytes; expected {expectedLength}.");

        uint frameId = header.FrameId;
        if (_useDisk)
        {
            lock (_diskLock)
            {
                if (_writeClosed)
                    throw new InvalidOperationException("Cannot add frames after the payload has been finalized.");
                if (!_diskFrameIds!.Add(frameId))
                    return false;
                long offset = (long)(frameId - 1) * _bytesPerFrame;
                _payloadStream!.Seek(offset, SeekOrigin.Begin);
                _payloadStream.Write(payload, 0, payload.Length);

                if (_persistent)
                {
                    // Payload before index: a crash between them loses a re-capturable frame, not a phantom one.
                    _payloadStream.Flush();
                    Span<byte> idBytes = stackalloc byte[sizeof(uint)];
                    BinaryPrimitives.WriteUInt32LittleEndian(idBytes, frameId);
                    _indexStream!.Write(idBytes);
                    _indexStream.Flush();
                }
            }
        }
        else if (!_frames!.TryAdd(frameId, payload))
        {
            return false;
        }

        if (frameId > LastAcceptedId)
            LastAcceptedId = frameId;
        ReceivedBytes += payload.Length;
        return true;
    }

    /// <summary>
    /// Discards every received frame, restarting the reception from empty (persisting mode only).
    /// Truncates the on-disk index so a fresh transfer of the same content starts clean.
    /// </summary>
    public void Reset()
    {
        if (!_persistent)
            throw new InvalidOperationException("Only a persisting assembler can be reset.");

        lock (_diskLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_writeClosed)
                throw new InvalidOperationException("Cannot reset after the payload has been finalized.");

            _diskFrameIds!.Clear();
            LastAcceptedId = 0;
            ReceivedBytes = 0;
            _indexStream!.SetLength(0);
            _indexStream.Flush();
        }
    }

    /// <summary>Rebuilds the received-id set (and derived counters) from the on-disk index.</summary>
    private void PreloadReceivedIds()
    {
        if (!File.Exists(_indexPath))
            return;

        var bytes = File.ReadAllBytes(_indexPath);
        for (int i = 0; i + sizeof(uint) <= bytes.Length; i += sizeof(uint))
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i));
            if (id >= 1 && id < _metadata.TotalFrames && _diskFrameIds!.Add(id))
            {
                if (id > LastAcceptedId)
                    LastAcceptedId = id;
                ReceivedBytes += ExpectedFrameLength(id);
            }
        }
    }

    /// <summary>
    /// Verifies the reassembled payload against the metadata SHA-256 without materializing it as a
    /// single array (streams the temp file in disk-backed mode).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when frames are missing.</exception>
    /// <exception cref="InvalidDataException">Thrown when the payload fails hash verification.</exception>
    public void Verify()
    {
        EnsureComplete();

        if (!_useDisk)
        {
            AssembleInMemory(verify: true);
            return;
        }

        // Close the write handle so the completed file can be read (and later copied / handed to
        // 7-Zip) without a file-sharing conflict, then hash it with a fresh read handle.
        FinalizeWriting();
        using var stream = File.OpenRead(_payloadFilePath!);
        if (!Sha256Helper.Verify(stream, _metadata.Sha256))
            throw new InvalidDataException(
                "Reassembled payload failed SHA-256 verification; one or more frames are corrupt.");
    }

    /// <summary>Flushes and closes the write stream so the payload file is free for readers. Idempotent.</summary>
    private void FinalizeWriting()
    {
        lock (_diskLock)
        {
            if (_writeClosed)
                return;
            _payloadStream!.Flush();
            _payloadStream.Dispose();
            _payloadStream = null;
            _indexStream?.Dispose();
            _indexStream = null;
            _writeClosed = true;
        }
    }

    /// <summary>
    /// Concatenates all frames in order and verifies the SHA-256 against the metadata (in-memory
    /// mode only). Disk-backed transfers should use <see cref="Verify"/> plus
    /// <see cref="PayloadFilePath"/> to avoid loading the whole payload into memory.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when frames are missing or the assembler is disk-backed.</exception>
    /// <exception cref="InvalidDataException">Thrown when the reassembled payload fails hash verification.</exception>
    public byte[] AssembleAndVerify()
    {
        if (_useDisk)
            throw new InvalidOperationException(
                "Payload is disk-backed; call Verify() and read PayloadFilePath instead of AssembleAndVerify().");

        EnsureComplete();
        return AssembleInMemory(verify: true);
    }

    /// <summary>
    /// Reassembles, verifies, and extracts the transfer to a directory: 7z payloads are
    /// decompressed, raw payloads are written under their original file name.
    /// </summary>
    /// <param name="targetDirectory">Directory to extract into.</param>
    /// <param name="compression">Compression service for 7z payloads.</param>
    /// <param name="progress">Optional 0-100 decompression progress sink.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path of the extracted file, or the target directory for archives.</returns>
    public async Task<string> ExtractAsync(
        string targetDirectory,
        CompressionService compression,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetDirectory);
        ArgumentNullException.ThrowIfNull(compression);

        Directory.CreateDirectory(targetDirectory);

        string? rawName = _metadata.PayloadType == PayloadType.Raw
            ? Path.GetFileName(_metadata.OriginalName) is { Length: > 0 } n ? n : "flux-payload.bin"
            : null;

        if (_useDisk)
        {
            Verify();
            if (rawName is not null)
            {
                var filePath = Path.Combine(targetDirectory, rawName);
                await CopyFileAsync(_payloadFilePath!, filePath, cancellationToken);
                return filePath;
            }

            await compression.DecompressFileAsync(_payloadFilePath!, targetDirectory, progress, cancellationToken);
            return targetDirectory;
        }

        var payload = AssembleAndVerify();
        if (rawName is not null)
        {
            var filePath = Path.Combine(targetDirectory, rawName);
            await File.WriteAllBytesAsync(filePath, payload, cancellationToken);
            return filePath;
        }

        await compression.DecompressAsync(payload, targetDirectory, progress, cancellationToken);
        return targetDirectory;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _payloadStream?.Dispose();
        _indexStream?.Dispose();

        // Persisting sessions are caller-owned and must survive; only the temp-file mode cleans up.
        if (_workDirectory is not null && !_persistent)
        {
            try { Directory.Delete(_workDirectory, recursive: true); }
            catch { /* best-effort temp cleanup */ }
        }
    }

    private void EnsureComplete()
    {
        if (!IsComplete)
            throw new InvalidOperationException(
                $"Transfer is incomplete: {MissingFrameIds.Count} of {ExpectedPayloadFrames} frames missing.");
    }

    private byte[] AssembleInMemory(bool verify)
    {
        var payload = new byte[_metadata.PayloadLength];
        int offset = 0;
        for (uint id = 1; id <= ExpectedPayloadFrames; id++)
        {
            _frames![id].CopyTo(payload.AsSpan(offset));
            offset += _frames[id].Length;
        }

        if (verify && !Sha256Helper.Verify(payload, _metadata.Sha256))
            throw new InvalidDataException(
                "Reassembled payload failed SHA-256 verification; one or more frames are corrupt.");

        return payload;
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
    }

    private int ExpectedFrameLength(uint frameId)
    {
        long remaining = _metadata.PayloadLength - (long)(frameId - 1) * _bytesPerFrame;
        return (int)Math.Clamp(remaining, 0, _bytesPerFrame);
    }
}
