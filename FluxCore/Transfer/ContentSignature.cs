using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FluxCore.Framing;

namespace FluxCore.Transfer;

/// <summary>
/// Computes the deterministic 32-byte signatures that key an encode session. The <b>payload</b>
/// signature covers only the source content and compression, so it is shared by every render
/// variant; the <b>render</b> signature covers the format spec (ECC, grid, tile size, colour); the
/// <b>combined</b> signature (painted into frame 0) identifies one content+spec transfer. Files are
/// signed by name, length, and streamed content; folders by their sorted file metadata (relative
/// path, length, last-write time) without reading contents.
/// </summary>
public static class ContentSignature
{
    /// <summary>
    /// Computes the payload signature of a source: content plus whether it will be 7z-compressed.
    /// Independent of tile/colour/ECC, so re-rendering the same source reuses the same payload.
    /// </summary>
    /// <param name="sourcePath">Path to a file or folder.</param>
    /// <param name="compress">Whether the source will be 7z-compressed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<byte[]> ComputePayloadSignatureAsync(
        string sourcePath, bool compress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        if (File.Exists(sourcePath))
        {
            await AppendFileAsync(hash, sourcePath, cancellationToken);
        }
        else if (Directory.Exists(sourcePath))
        {
            AppendFolder(hash, sourcePath);
        }
        else
        {
            throw new FileNotFoundException($"Source not found: {sourcePath}");
        }

        hash.AppendData([(byte)(compress ? 1 : 0)]);
        return hash.GetHashAndReset();
    }

    /// <summary>Computes the render signature from the format spec (ECC, grid, tile size, colour, palette kind, version).</summary>
    /// <param name="options">Encode options whose render-affecting fields are hashed.</param>
    public static byte[] ComputeRenderSignature(EncodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData([(byte)options.EccLevel, FrameFormat.Version, (byte)options.PaletteKind]);
        AppendInt64(hash, options.GridWidthTiles);
        AppendInt64(hash, options.GridHeightTiles);
        AppendInt64(hash, options.TilePixelSize);
        AppendInt64(hash, options.ColorCount);
        return hash.GetHashAndReset();
    }

    /// <summary>Combines a payload and render signature into the transfer signature painted into frame 0.</summary>
    /// <param name="payloadSignature">Payload signature (content + compression).</param>
    /// <param name="renderSignature">Render signature (format spec).</param>
    public static byte[] Combine(byte[] payloadSignature, byte[] renderSignature)
    {
        ArgumentNullException.ThrowIfNull(payloadSignature);
        ArgumentNullException.ThrowIfNull(renderSignature);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(payloadSignature);
        hash.AppendData(renderSignature);
        return hash.GetHashAndReset();
    }

    /// <summary>Derives the session folder name from a signature (first 16 hex characters).</summary>
    /// <param name="signature">32-byte content signature.</param>
    public static string ToSessionName(byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        if (signature.Length != 32)
            throw new ArgumentException("Signature must be 32 bytes.", nameof(signature));

        return Convert.ToHexString(signature.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static async Task AppendFileAsync(IncrementalHash hash, string filePath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(filePath);
        AppendRecord(hash, info.Name, info.Length);

        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer.AsSpan(0, read));
        }
    }

    private static void AppendFolder(IncrementalHash hash, string folderPath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        AppendRecord(hash, Path.GetFileName(root), 0);

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => (RelativePath: Path.GetRelativePath(root, path), Info: new FileInfo(path)))
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal);

        foreach (var (relativePath, info) in files)
        {
            AppendRecord(hash, relativePath, info.Length);
            AppendInt64(hash, info.LastWriteTimeUtc.Ticks);
        }
    }

    private static void AppendRecord(IncrementalHash hash, string name, long length)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(name));
        hash.AppendData([0]);
        AppendInt64(hash, length);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }
}
