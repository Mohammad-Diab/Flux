using System.IO;

namespace FluxCast.Services;

/// <summary>
/// Result of validating an encode source.
/// </summary>
/// <param name="IsFolder">Whether the source is a folder.</param>
/// <param name="TotalBytes">Total content size in bytes.</param>
/// <param name="FileCount">Number of files (1 for a single file).</param>
/// <param name="Error">Human-readable rejection reason, or null when acceptable.</param>
public sealed record SourceInfo(bool IsFolder, long TotalBytes, int FileCount, string? Error)
{
    /// <summary>Gets a value indicating whether the source can be encoded.</summary>
    public bool IsValid => Error is null;
}

/// <summary>
/// Validates encode sources against transfer limits before a session starts.
/// </summary>
public sealed class SourceValidator
{
    /// <summary>Maximum size of a single file source.</summary>
    public const long MaxFileBytes = 10L * 1024 * 1024 * 1024;

    /// <summary>Maximum total size of a folder source.</summary>
    public const long MaxFolderBytes = 50L * 1024 * 1024 * 1024;

    /// <summary>Maximum number of files in a folder source.</summary>
    public const int MaxFolderFiles = 100_000;

    /// <summary>
    /// Validates a file or folder source, computing its size on a background thread.
    /// </summary>
    /// <param name="path">Source path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SourceInfo> ValidateAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Validate(path), cancellationToken);

    private static SourceInfo Validate(string path)
    {
        if (File.Exists(path))
        {
            long length = new FileInfo(path).Length;
            if (length == 0)
                return new SourceInfo(false, 0, 1, "The file is empty.");
            if (length > MaxFileBytes)
                return new SourceInfo(false, length, 1,
                    $"File is {length / (1024.0 * 1024 * 1024):F1} GB; the limit is 10 GB.");

            return new SourceInfo(false, length, 1, null);
        }

        if (Directory.Exists(path))
        {
            long totalBytes = 0;
            int fileCount = 0;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                fileCount++;
                if (fileCount > MaxFolderFiles)
                    return new SourceInfo(true, totalBytes, fileCount,
                        $"Folder has more than {MaxFolderFiles:N0} files.");

                totalBytes += new FileInfo(file).Length;
                if (totalBytes > MaxFolderBytes)
                    return new SourceInfo(true, totalBytes, fileCount,
                        "Folder is larger than the 50 GB limit.");
            }

            if (fileCount == 0)
                return new SourceInfo(true, 0, 0, "The folder is empty.");

            return new SourceInfo(true, totalBytes, fileCount, null);
        }

        return new SourceInfo(false, 0, 0, "The selected path no longer exists.");
    }
}
