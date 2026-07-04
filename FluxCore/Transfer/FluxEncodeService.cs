using System.Text.Json;
using FluxCore.Compression;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;
using FluxCore.Imaging;
using Microsoft.Extensions.Logging;

namespace FluxCore.Transfer;

/// <summary>
/// Encodes a file or folder into a resumable frame session on disk:
/// {sessionRoot}/{signature}/payload.dat + manifest.json + frames/frame_NNNNNN.png.
/// Re-running with the same source reuses the compressed payload (7z output is not
/// byte-deterministic, so recompressing would invalidate rendered frames) and renders
/// only the frames that are missing.
/// </summary>
public sealed class FluxEncodeService
{
    private const string PayloadFileName = "payload.dat";
    private const string ManifestFileName = "manifest.json";
    private const string FramesFolderName = "frames";

    private readonly CompressionService _compression;
    private readonly ILogger<FluxEncodeService>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FluxEncodeService"/> class.
    /// </summary>
    /// <param name="compression">Compression service for 7z payloads.</param>
    /// <param name="logger">Optional logger.</param>
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

        var signature = await ContentSignature.ComputeAsync(sourcePath, options, cancellationToken);
        var sessionDirectory = Path.Combine(sessionRoot, ContentSignature.ToSessionName(signature));
        var framesDirectory = Path.Combine(sessionDirectory, FramesFolderName);
        var payloadPath = Path.Combine(sessionDirectory, PayloadFileName);
        var manifestPath = Path.Combine(sessionDirectory, ManifestFileName);
        Directory.CreateDirectory(framesDirectory);

        var (payload, payloadType, payloadReused) = await LoadOrCompressPayloadAsync(
            sourcePath, options, signature, payloadPath, manifestPath, framesDirectory, progress, cancellationToken);

        int bytesPerFrame = options.EccLevel.PayloadBytesPerFrame();
        uint payloadFrames = (uint)Math.Max(1, (payload.Length + bytesPerFrame - 1) / bytesPerFrame);
        uint totalFrames = payloadFrames + 1;

        var metadata = new MetadataPayload(
            sha256: Sha256Helper.ComputeHash(payload),
            payloadType: payloadType,
            eccLevel: options.EccLevel,
            totalFrames: totalFrames,
            payloadLength: payload.Length,
            originalName: SourceName(sourcePath),
            originalLength: SourceLength(sourcePath),
            contentSignature: signature,
            colorMap: ColorMap.Default);

        int rendered = await RenderMissingFramesAsync(
            metadata, payload, options.EccLevel, framesDirectory, progress, cancellationToken);

        progress?.Report(new EncodeProgress(EncodePhase.Completed, (int)totalFrames, (int)totalFrames));
        _logger?.LogInformation(
            "Encode session {Session}: {Total} frames, {Rendered} rendered this run, payload reused: {Reused}",
            sessionDirectory, totalFrames, rendered, payloadReused);

        return new EncodeSessionResult(
            sessionDirectory, framesDirectory, totalFrames, payload.Length, signature, payloadReused, rendered);
    }

    private async Task<(byte[] Payload, PayloadType Type, bool Reused)> LoadOrCompressPayloadAsync(
        string sourcePath,
        EncodeOptions options,
        byte[] signature,
        string payloadPath,
        string manifestPath,
        string framesDirectory,
        IProgress<EncodeProgress>? progress,
        CancellationToken cancellationToken)
    {
        var manifest = TryReadManifest(manifestPath);
        if (manifest is not null &&
            manifest.SignatureHex == Convert.ToHexString(signature) &&
            File.Exists(payloadPath))
        {
            var candidate = await File.ReadAllBytesAsync(payloadPath, cancellationToken);
            if (Sha256Helper.Verify(candidate, Convert.FromHexString(manifest.PayloadSha256Hex)))
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

        foreach (var stale in Directory.EnumerateFiles(framesDirectory, "frame_*.png"))
        {
            File.Delete(stale);
        }

        await File.WriteAllBytesAsync(payloadPath, payload, cancellationToken);
        var freshManifest = new SessionManifest(
            FrameFormat.Version,
            Convert.ToHexString(signature),
            payloadType,
            Convert.ToHexString(Sha256Helper.ComputeHash(payload)),
            payload.Length);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(freshManifest), cancellationToken);

        return (payload, payloadType, false);
    }

    private static async Task<int> RenderMissingFramesAsync(
        MetadataPayload metadata,
        byte[] payload,
        EccLevel eccLevel,
        string framesDirectory,
        IProgress<EncodeProgress>? progress,
        CancellationToken cancellationToken)
    {
        int bytesPerFrame = eccLevel.PayloadBytesPerFrame();
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
                    var png = RenderFrame(metadata, payload, eccLevel, bytesPerFrame, (uint)id, totalFrames);
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
        MetadataPayload metadata, byte[] payload, EccLevel eccLevel, int bytesPerFrame, uint frameId, uint totalFrames)
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
            map = FrameEncoder.BuildFrame(frameId, totalFrames, payload.AsSpan(offset, length), eccLevel);
        }

        return FrameRenderer.RenderPng(map, ColorMap.Default);
    }

    private static SessionManifest? TryReadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(manifestPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SourceName(string sourcePath) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath)));

    private static long SourceLength(string sourcePath)
    {
        if (File.Exists(sourcePath))
            return new FileInfo(sourcePath).Length;

        return Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
    }

    private sealed record SessionManifest(
        byte FormatVersion,
        string SignatureHex,
        PayloadType PayloadType,
        string PayloadSha256Hex,
        long PayloadLength);
}
