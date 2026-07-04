using System.IO;
using FluxCore.Compression;
using FluxCore.Decoding;
using FluxCore.Framing;
using FluxCore.Imaging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace FluxRead.Services;

/// <summary>One row of the decode results grid.</summary>
/// <param name="FileName">Frame file name.</param>
/// <param name="FrameId">Decoded frame id, if the header was recovered.</param>
/// <param name="Status">Outcome word (Success / WrongFrame / reason).</param>
/// <param name="Detail">Payload size, corrected errors, or failure detail.</param>
/// <param name="Success">Whether the frame decoded and was accepted.</param>
public sealed record FrameRow(string FileName, string FrameId, string Status, string Detail, bool Success);

/// <summary>Progress of a folder decode.</summary>
/// <param name="Completed">Frames processed so far.</param>
/// <param name="Total">Total frame files.</param>
public sealed record DecodeProgress(int Completed, int Total);

/// <summary>Outcome of decoding a frames folder.</summary>
/// <param name="Metadata">Decoded frame-0 metadata, if readable.</param>
/// <param name="Assembler">Payload assembler holding accepted frames, if metadata was read.</param>
/// <param name="Rows">Per-frame result rows.</param>
/// <param name="IsComplete">Whether every payload frame was received.</param>
/// <param name="Error">Fatal error message, or null on a readable transfer.</param>
public sealed record FolderDecodeResult(
    MetadataPayload? Metadata,
    PayloadAssembler? Assembler,
    IReadOnlyList<FrameRow> Rows,
    bool IsComplete,
    string? Error);

/// <summary>
/// Decodes a folder of frame PNGs into a payload and extracts it. Shared by folder-decode mode
/// and (later) the live optical capture loop. Frame 0 is decoded with the 8-color metadata
/// path; payload frames use the palette path; a <see cref="PayloadAssembler"/> verifies the
/// reassembled payload against the frame-0 SHA-256 before extraction.
/// </summary>
public sealed class DecodePipelineService
{
    private readonly CompressionService _compression;
    private readonly ILogger<DecodePipelineService>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecodePipelineService"/> class.
    /// </summary>
    /// <param name="compression">Compression service for 7z payloads.</param>
    /// <param name="logger">Optional logger.</param>
    public DecodePipelineService(CompressionService compression, ILogger<DecodePipelineService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(compression);
        _compression = compression;
        _logger = logger;
    }

    /// <summary>
    /// Decodes every frame_*.png in a folder.
    /// </summary>
    /// <param name="framesDirectory">Folder containing frame PNGs.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<FolderDecodeResult> DecodeFolderAsync(
        string framesDirectory,
        IProgress<DecodeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(framesDirectory);
        return Task.Run(() => DecodeFolder(framesDirectory, progress, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Verifies the reassembled payload and writes it: raw payloads to a chosen file, 7z payloads
    /// decompressed into a chosen folder.
    /// </summary>
    /// <param name="result">A readable, complete decode result.</param>
    /// <param name="outputPath">Target file (raw) or folder (7z).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveAsync(FolderDecodeResult result, string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Assembler is null || result.Metadata is null)
            throw new InvalidOperationException("Nothing to save: the transfer was not readable.");

        var payload = result.Assembler.AssembleAndVerify();

        if (result.Metadata.PayloadType == PayloadType.Raw)
        {
            await File.WriteAllBytesAsync(outputPath, payload, cancellationToken);
        }
        else
        {
            Directory.CreateDirectory(outputPath);
            await _compression.DecompressAsync(payload, outputPath, cancellationToken);
        }

        _logger?.LogInformation("Saved transfer '{Name}' to {Path}", result.Metadata.OriginalName, outputPath);
    }

    private FolderDecodeResult DecodeFolder(
        string framesDirectory, IProgress<DecodeProgress>? progress, CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(framesDirectory, "frame_*.png")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
            return new FolderDecodeResult(null, null, [], false, "No frame_*.png files found in the folder.");

        var decoder = new FrameDecoder(ColorMap.Default);
        var rows = new List<FrameRow>(files.Length);

        MetadataPayload metadata;
        try
        {
            using var frame0 = SKBitmap.Decode(files[0]);
            var meta = decoder.DecodeMetadataFrame(frame0);
            if (meta.Status != DecodeStatus.Success)
            {
                rows.Add(new FrameRow(Path.GetFileName(files[0]), "0", meta.FailureReason.ToString(), "metadata frame", false));
                return new FolderDecodeResult(null, null, rows, false, $"Frame 0 (metadata) could not be decoded: {meta.FailureReason}.");
            }

            metadata = MetadataPayload.Deserialize(meta.Payload!);
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            return new FolderDecodeResult(null, null, rows, false, $"Frame 0 metadata is invalid: {ex.Message}");
        }

        if (!metadata.MatchesFrameFormat())
            return new FolderDecodeResult(metadata, null, rows, false, "Frame 0 was made by an incompatible Flux version.");

        rows.Add(new FrameRow(Path.GetFileName(files[0]), "0", "Metadata",
            $"{metadata.OriginalName} · {metadata.TotalFrames} frames · {metadata.PayloadType}", true));
        progress?.Report(new DecodeProgress(1, files.Length));

        var assembler = new PayloadAssembler(metadata);
        for (int i = 1; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(DecodePayloadFrame(decoder, assembler, files[i]));
            progress?.Report(new DecodeProgress(i + 1, files.Length));
        }

        return new FolderDecodeResult(metadata, assembler, rows, assembler.IsComplete, null);
    }

    private static FrameRow DecodePayloadFrame(FrameDecoder decoder, PayloadAssembler assembler, string file)
    {
        var name = Path.GetFileName(file);
        using var bitmap = SKBitmap.Decode(file);
        var result = decoder.Decode(bitmap);

        if (result.Status != DecodeStatus.Success)
        {
            var id = result.Header?.FrameId.ToString() ?? "?";
            return new FrameRow(name, id, result.FailureReason.ToString(), "", false);
        }

        var header = result.Header!.Value;
        try
        {
            bool added = assembler.AddFrame(header, result.Payload!);
            var detail = $"{result.Payload!.Length} bytes, {result.Diagnostics.CorrectedErrors} corrected";
            return new FrameRow(name, header.FrameId.ToString(), added ? "Success" : "Duplicate", detail, true);
        }
        catch (ArgumentException ex)
        {
            return new FrameRow(name, header.FrameId.ToString(), "Inconsistent", ex.Message, false);
        }
    }
}
