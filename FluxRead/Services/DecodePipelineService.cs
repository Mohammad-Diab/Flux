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

    public DecodePipelineService(CompressionService compression, ILogger<DecodePipelineService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(compression);
        _compression = compression;
        _logger = logger;
    }

    /// <summary>Decodes every frame_*.png in a folder.</summary>
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
    public async Task SaveAsync(
        PayloadAssembler assembler,
        MetadataPayload metadata,
        string outputPath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembler);
        ArgumentNullException.ThrowIfNull(metadata);

        bool raw = metadata.PayloadType == PayloadType.Raw;

        if (assembler.IsDiskBacked)
        {
            // Large transfer: verify by streaming, then save straight from the temp payload file.
            assembler.Verify();
            if (raw)
            {
                await using var input = new FileStream(assembler.PayloadFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, cancellationToken);
            }
            else
            {
                Directory.CreateDirectory(outputPath);
                await _compression.DecompressFileAsync(assembler.PayloadFilePath, outputPath, progress, cancellationToken);
            }
        }
        else
        {
            var payload = assembler.AssembleAndVerify();
            if (raw)
            {
                await File.WriteAllBytesAsync(outputPath, payload, cancellationToken);
            }
            else
            {
                Directory.CreateDirectory(outputPath);
                await _compression.DecompressAsync(payload, outputPath, progress, cancellationToken);
            }
        }

        _logger?.LogInformation("Saved transfer '{Name}' to {Path}", metadata.OriginalName, outputPath);
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

        if (!metadata.TryBuildLayout(out var layout))
            return new FolderDecodeResult(metadata, null, rows, false, "Frame 0 was made by an incompatible Flux version.");

        rows.Add(new FrameRow(Path.GetFileName(files[0]), "0", "Metadata",
            $"{metadata.OriginalName} · {metadata.TotalFrames} frames · {metadata.PayloadType}", true));
        progress?.Report(new DecodeProgress(1, files.Length));

        // Payload frames adopt the transfer's palette; frame 0 stayed on the palette-independent cube path.
        var payloadDecoder = metadata.ColorCount == 256 ? decoder : new FrameDecoder(ColorMap.FromCount(metadata.ColorCount, metadata.PaletteKind));
        int bitsPerTile = metadata.BitsPerTile;

        var assembler = new PayloadAssembler(metadata);
        for (int i = 1; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(DecodePayloadFrame(payloadDecoder, assembler, files[i], layout, bitsPerTile));
            progress?.Report(new DecodeProgress(i + 1, files.Length));
        }

        return new FolderDecodeResult(metadata, assembler, rows, assembler.IsComplete, null);
    }

    private static FrameRow DecodePayloadFrame(
        FrameDecoder decoder, PayloadAssembler assembler, string file, FrameLayout layout, int bitsPerTile)
    {
        var name = Path.GetFileName(file);
        using var bitmap = SKBitmap.Decode(file);
        var result = decoder.Decode(bitmap, bitsPerTile: bitsPerTile, layout: layout);

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
