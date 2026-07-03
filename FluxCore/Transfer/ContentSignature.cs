using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FluxCore.Framing;

namespace FluxCore.Transfer;

/// <summary>
/// Computes a deterministic 32-byte signature identifying a source and its encode options,
/// used to name session folders so re-encoding the same source resumes the existing session.
/// Files are signed by name, length, and streamed content; folders by their sorted file
/// metadata (relative path, length, last-write time) without reading contents.
/// </summary>
public static class ContentSignature
{
    /// <summary>
    /// Computes the signature of a file or folder combined with the encode options.
    /// </summary>
    /// <param name="sourcePath">Path to a file or folder.</param>
    /// <param name="options">Encode options that affect frame output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<byte[]> ComputeAsync(
        string sourcePath, EncodeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(options);

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

        hash.AppendData([(byte)options.EccLevel, (byte)(options.Compress ? 1 : 0), FrameFormat.Version]);

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
