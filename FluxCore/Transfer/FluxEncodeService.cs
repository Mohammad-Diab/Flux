using System.Text.Json;
using FluxCore.Compression;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;
using FluxCore.IO;
using FluxCore.Imaging;
using Microsoft.Extensions.Logging;

namespace FluxCore.Transfer;

/// <summary>
/// Encodes a file or folder into a resumable frame session on disk, split so one compressed payload
/// feeds many render variants:
/// {sessionRoot}/{payloadKey}/payload.dat + payload.json,
/// and per variant {payloadKey}/renders/{renderKey}/manifest.json + frames/frame_NNNNNN.png.
/// The payload is keyed by content+compression only, so re-rendering the same source at different
/// tile/colour/ECC settings reuses payload.dat (7z output is not byte-deterministic) and renders
/// only the frames that are missing under the new render key.
/// </summary>
public sealed class FluxEncodeService
{
    private readonly CompressionService _compression;
    private readonly ILogger<FluxEncodeService>? _logger;

    /// <summary>Creates the service over a compression service for 7z payloads.</summary>
    public FluxEncodeService(CompressionService compression, ILogger<FluxEncodeService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(compression);
        _compression = compression;
        _logger = logger;
    }

    /// <summary>Gets the canonical frame file name for a frame id.</summary>
    /// <param name="frameId">Frame id.</param>
    public static string FrameFileName(uint frameId) => $"frame_{frameId:D6}.png";

    /// <summary>
    /// Encodes the source into a frame session, resuming any existing session for the same
    /// source and options.
    /// </summary>
    /// <param name="sourcePath">File or folder to encode.</param>
    /// <param name="sessionRoot">Directory under which session folders are created.</param>
    /// <param name="options">Encode options.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<EncodeSessionResult> EncodeAsync(
        string sourcePath,
        string sessionRoot,
        EncodeOptions options,
        IProgress<EncodeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(sessionRoot);
        ArgumentNullException.ThrowIfNull(options);

        var layout = BuildPayloadLayout(options);

        var payloadSignature = await ContentSignature.ComputePayloadSignatureAsync(
            sourcePath, options.Compress, cancellationToken);
        var renderSignature = ContentSignature.ComputeRenderSignature(options);
        var combinedSignature = ContentSignature.Combine(payloadSignature, renderSignature);

        var payloadDirectory = Path.Combine(sessionRoot, ContentSignature.ToSessionName(payloadSignature));
        var renderDirectory = Path.Combine(
            payloadDirectory, SessionLayout.RendersFolderName, ContentSignature.ToSessionName(renderSignature));
        var framesDirectory = Path.Combine(renderDirectory, SessionLayout.FramesFolderName);
        var payloadPath = Path.Combine(payloadDirectory, SessionLayout.PayloadFileName);
        var payloadManifestPath = Path.Combine(payloadDirectory, SessionLayout.PayloadManifestFileName);
        var renderManifestPath = Path.Combine(renderDirectory, SessionLayout.RenderManifestFileName);
        Directory.CreateDirectory(payloadDirectory);

        var (payload, payloadType, payloadReused) = await LoadOrCompressPayloadAsync(
            sourcePath, options, payloadSignature, payloadPath, payloadManifestPath, payloadDirectory, progress, cancellationToken);

        // After a possible recompress (which wipes stale renders) create the render's frames folder.
        Directory.CreateDirectory(framesDirectory);

        int bitsPerTile = PaletteGenerator.BitsForCount(options.ColorCount);
        int bytesPerFrame = options.EccLevel.PayloadBytesPerFrame(layout.CodewordsForBits(bitsPerTile));
        uint payloadFrames = (uint)Math.Max(1, (payload.Length + bytesPerFrame - 1) / bytesPerFrame);
        uint totalFrames = payloadFrames + 1;

        var payloadSha = Sha256Helper.ComputeHash(payload);
        var metadata = new MetadataPayload(
            sha256: payloadSha,
            payloadType: payloadType,
            eccLevel: options.EccLevel,
            totalFrames: totalFrames,
            payloadLength: payload.Length,
            originalName: SourceName(sourcePath),
            originalLength: PathSize.GetTotalBytes(sourcePath),
            contentSignature: combinedSignature,
            colorCount: options.ColorCount)
        {
            GridWidthTiles = (ushort)layout.GridWidthTiles,
            GridHeightTiles = (ushort)layout.GridHeightTiles,
            TilePixelSize = (byte)layout.TilePixelSize,
        };

        int rendered = await RenderMissingFramesAsync(
            metadata, payload, layout, framesDirectory, progress, cancellationToken);

        await WritePayloadManifestAsync(
            payloadManifestPath, payloadSignature, payloadType, payloadSha, payload.Length, sourcePath, cancellationToken);
        await WriteRenderManifestAsync(
            renderManifestPath, renderSignature, combinedSignature, options, layout, totalFrames, cancellationToken);

        progress?.Report(new EncodeProgress(EncodePhase.Completed, (int)totalFrames, (int)totalFrames));
        _logger?.LogInformation(
            "Encode session {Render}: {Total} frames, {Rendered} rendered this run, payload reused: {Reused}",
            renderDirectory, totalFrames, rendered, payloadReused);

        return new EncodeSessionResult(
            renderDirectory, payloadDirectory, framesDirectory, totalFrames, payload.Length, combinedSignature, payloadReused, rendered);
    }

    private async Task<(byte[] Payload, PayloadType Type, bool Reused)> LoadOrCompressPayloadAsync(
        string sourcePath,
        EncodeOptions options,
        byte[] payloadSignature,
        string payloadPath,
        string payloadManifestPath,
        string payloadDirectory,
        IProgress<EncodeProgress>? progress,
        CancellationToken cancellationToken)
    {
        var manifest = PayloadManifest.TryRead(payloadManifestPath);
        if (manifest is not null &&
            manifest.PayloadSignatureHex == Sha256Helper.ToHexString(payloadSignature) &&
            File.Exists(payloadPath))
        {
            var candidate = await File.ReadAllBytesAsync(payloadPath, cancellationToken);
            if (Sha256Helper.Verify(candidate, Sha256Helper.FromHexString(manifest.PayloadSha256Hex)))
            {
                _logger?.LogInformation("Reusing existing payload ({Length} bytes).", candidate.Length);
                return (candidate, manifest.PayloadType, true);
            }

            _logger?.LogWarning("Existing payload failed verification; recompressing.");
        }

        progress?.Report(new EncodeProgress(EncodePhase.Compressing));

        bool isFolder = Directory.Exists(sourcePath);
        PayloadType payloadType;
        byte[] payload;

        if (isFolder || options.Compress)
        {
            var compressionProgress = progress is null
                ? null
                : new Progress<int>(pct => progress.Report(new EncodeProgress(EncodePhase.Compressing, CompressionPercent: pct)));
            var result = await _compression.CompressAsync(sourcePath, compressionProgress, cancellationToken);
            payload = result.Data;
            payloadType = PayloadType.SevenZip;
        }
        else
        {
            payload = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
            payloadType = PayloadType.Raw;
        }

        // A fresh payload invalidates every existing rendering of it.
        var rendersRoot = Path.Combine(payloadDirectory, SessionLayout.RendersFolderName);
        if (Directory.Exists(rendersRoot))
            Directory.Delete(rendersRoot, recursive: true);

        await File.WriteAllBytesAsync(payloadPath, payload, cancellationToken);
        // Minimal manifest now so an interrupted render can still reuse the payload; enriched at the end.
        var freshManifest = new PayloadManifest(
            FrameFormat.Version,
            Sha256Helper.ToHexString(payloadSignature),
            payloadType,
            Sha256Helper.ToHexString(Sha256Helper.ComputeHash(payload)),
            payload.Length);
        await File.WriteAllTextAsync(payloadManifestPath, JsonSerializer.Serialize(freshManifest), cancellationToken);

        return (payload, payloadType, false);
    }

    private static async Task WritePayloadManifestAsync(
        string manifestPath,
        byte[] payloadSignature,
        PayloadType payloadType,
        byte[] payloadSha,
        long payloadLength,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var existing = PayloadManifest.TryRead(manifestPath);
        var manifest = new PayloadManifest(
            FrameFormat.Version,
            Sha256Helper.ToHexString(payloadSignature),
            payloadType,
            Sha256Helper.ToHexString(payloadSha),
            payloadLength,
            SourcePath: Path.GetFullPath(sourcePath),
            DisplayName: SourceName(sourcePath),
            SourceKind: Directory.Exists(sourcePath) ? SourceKind.Folder : SourceKind.File,
            CreatedUtc: existing?.CreatedUtc ?? DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest), cancellationToken);
    }

    private static async Task WriteRenderManifestAsync(
        string manifestPath,
        byte[] renderSignature,
        byte[] combinedSignature,
        EncodeOptions options,
        FrameLayout layout,
        uint totalFrames,
        CancellationToken cancellationToken)
    {
        var existing = RenderManifest.TryRead(manifestPath);
        var manifest = new RenderManifest(
            FrameFormat.Version,
            Sha256Helper.ToHexString(renderSignature),
            Sha256Helper.ToHexString(combinedSignature),
            options.EccLevel,
            layout.GridWidthTiles,
            layout.GridHeightTiles,
            layout.TilePixelSize,
            options.ColorCount,
            totalFrames,
            CreatedUtc: existing?.CreatedUtc ?? DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest), cancellationToken);
    }

    private static async Task<int> RenderMissingFramesAsync(
        MetadataPayload metadata,
        byte[] payload,
        FrameLayout layout,
        string framesDirectory,
        IProgress<EncodeProgress>? progress,
        CancellationToken cancellationToken)
    {
        int bitsPerTile = PaletteGenerator.BitsForCount(metadata.ColorCount);
        int bytesPerFrame = metadata.EccLevel.PayloadBytesPerFrame(layout.CodewordsForBits(bitsPerTile));
        var colorMap = metadata.ColorCount == 256 ? ColorMap.Default : ColorMap.FromCount(metadata.ColorCount);
        uint totalFrames = metadata.TotalFrames;
        int rendered = 0;
        int completed = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, (int)totalFrames),
            new ParallelOptions { CancellationToken = cancellationToken },
            async (id, token) =>
            {
                var framePath = Path.Combine(framesDirectory, FrameFileName((uint)id));
                if (!File.Exists(framePath))
                {
                    var png = RenderFrame(metadata, payload, bytesPerFrame, layout, colorMap, bitsPerTile, (uint)id, totalFrames);
                    var tempPath = framePath + ".tmp";
                    await File.WriteAllBytesAsync(tempPath, png, token);
                    File.Move(tempPath, framePath, overwrite: true);
                    Interlocked.Increment(ref rendered);
                }

                int done = Interlocked.Increment(ref completed);
                progress?.Report(new EncodeProgress(EncodePhase.RenderingFrames, done, (int)totalFrames));
            });

        return rendered;
    }

    private static byte[] RenderFrame(
        MetadataPayload metadata, byte[] payload, int bytesPerFrame, FrameLayout layout,
        ColorMap colorMap, int bitsPerTile, uint frameId, uint totalFrames)
    {
        FrameTileMap map;
        if (frameId == 0)
        {
            map = FrameEncoder.BuildMetadataFrame(metadata.Serialize(), totalFrames);
        }
        else
        {
            int offset = (int)(frameId - 1) * bytesPerFrame;
            int length = Math.Clamp(payload.Length - offset, 0, bytesPerFrame);
            map = FrameEncoder.BuildFrame(frameId, totalFrames, payload.AsSpan(offset, length), metadata.EccLevel, bitsPerTile, layout);
        }

        return FrameRenderer.RenderPng(map, colorMap);
    }

    private static FrameLayout BuildPayloadLayout(EncodeOptions options)
    {
        var layout = new FrameLayout(options.GridWidthTiles, options.GridHeightTiles, options.TilePixelSize);
        int codewords = layout.CodewordsForBits(PaletteGenerator.BitsForCount(options.ColorCount));
        int bytesPerFrame = options.EccLevel.PayloadBytesPerFrame(codewords);
        if (bytesPerFrame > ushort.MaxValue)
            throw new ArgumentException(
                $"Grid {options.GridWidthTiles}×{options.GridHeightTiles} at {options.EccLevel} ECC needs {bytesPerFrame} bytes per frame, over the {ushort.MaxValue}-byte per-frame limit.",
                nameof(options));
        return layout;
    }

    private static string SourceName(string sourcePath) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath)));
}
