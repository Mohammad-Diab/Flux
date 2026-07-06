using FluxCore.IO;
using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Readers;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace FluxCore.Compression;

/// <summary>
/// Provides compression and decompression using external 7z utility (preferred) or SharpCompress (fallback).
/// Also supports raw (uncompressed) passthrough mode.
/// </summary>
public sealed class CompressionService
{
    private static readonly Regex ProgressPercentPattern = new(@"(\d{1,3})%", RegexOptions.Compiled);

    private readonly ILogger<CompressionService>? _logger;
    private readonly string? _sevenZipPath;
    private readonly bool _prefer7zExe;
    private readonly bool _has7zExe;

    /// <summary>
    /// Gets a value indicating whether 7z.exe is available and will be used.
    /// </summary>
    public bool Using7zExe => _has7zExe && _prefer7zExe;

    /// <summary>
    /// Gets the compression method being used.
    /// </summary>
    public string CompressionMethod => Using7zExe ? "7z.exe (fast, best ratio)" : "Built-in (slower, good ratio)";

    /// <summary>Creates the service; a null <paramref name="sevenZipPath"/> searches common locations and PATH.</summary>
    public CompressionService(string? sevenZipPath = null, bool prefer7zExe = true, ILogger<CompressionService>? logger = null)
    {
        _logger = logger;
        _prefer7zExe = prefer7zExe;
        _sevenZipPath = sevenZipPath ?? Find7zExecutable();
        _has7zExe = !string.IsNullOrEmpty(_sevenZipPath) && File.Exists(_sevenZipPath);

        if (_prefer7zExe && !_has7zExe)
        {
            _logger?.LogWarning(
                "7z.exe not found. Falling back to built-in compression (40-60% slower, slightly lower ratio).\n" +
                "For best performance, install 7-Zip from: https://www.7-zip.org/download.html\n" +
                "Searched: {Path}",
                _sevenZipPath ?? "PATH");
        }
        else if (_has7zExe)
        {
            _logger?.LogInformation("Using 7z.exe for compression: {Path}", _sevenZipPath);
        }
        else
        {
            _logger?.LogInformation("Using built-in SharpCompress for compression.");
        }
    }

    /// <summary>Compresses a file or folder; progress is only reported when using 7z.exe.</summary>
    public async Task<CompressionResult> CompressAsync(
        string sourcePath, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            throw new FileNotFoundException($"Source not found: {sourcePath}");

        return Using7zExe
            ? await Compress7zExeAsync(sourcePath, progress, cancellationToken)
            : await CompressSharpCompressAsync(sourcePath, cancellationToken);
    }

    /// <summary>Decompresses in-memory 7z data; progress is only reported when using 7z.exe.</summary>
    public async Task DecompressAsync(
        byte[] compressedData, string targetDirectory, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compressedData);
        ArgumentNullException.ThrowIfNull(targetDirectory);

        // Stage to a temp archive so decompression always streams from disk (one code path).
        using var tempArchive = new TempFile();
        await File.WriteAllBytesAsync(tempArchive.Path, compressedData, cancellationToken);
        await DecompressFileAsync(tempArchive.Path, targetDirectory, progress, cancellationToken);
    }

    /// <summary>
    /// Decompresses a 7z archive streaming from disk (no full-payload buffer in memory), used by
    /// the disk-backed assembler for large transfers.
    /// </summary>
    public async Task DecompressFileAsync(
        string archivePath, string targetDirectory, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archivePath);
        ArgumentNullException.ThrowIfNull(targetDirectory);

        if (Using7zExe)
        {
            try
            {
                await Decompress7zExeFileAsync(archivePath, targetDirectory, progress, cancellationToken);
                return;
            }
            catch (CompressionException ex)
            {
                _logger?.LogWarning(ex, "7z.exe decompression failed, falling back to built-in decompression.");
            }
        }

        await DecompressSharpCompressFileAsync(archivePath, targetDirectory, cancellationToken);
    }

    /// <summary>Creates a raw (uncompressed) passthrough result.</summary>
    public CompressionResult CreateRaw(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new CompressionResult(data, data.Length);
    }

    #region 7z.exe Implementation

    private async Task<CompressionResult> Compress7zExeAsync(
        string sourcePath, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using var tempArchive = new TempFile();

        long originalSize = PathSize.GetTotalBytes(sourcePath);
        _logger?.LogInformation("Compressing {Source} ({Size} bytes) with 7z.exe", sourcePath, originalSize);

        // Use maximum compression: -mx=9, LZMA2, solid archive. -bsp1 streams progress
        // percentages to stdout. Archive the folder (or file) itself so its top-level name is
        // preserved — extraction recreates "<folder>/…" rather than dumping loose contents.
        var arguments = $"a -t7z -mx=9 -m0=lzma2 -ms=on -bsp1 \"{tempArchive.Path}\" \"{sourcePath}\"";

        await Run7zAsync(arguments, progress, cancellationToken);

        if (!File.Exists(tempArchive.Path))
            throw new CompressionException("7z compression produced no output archive.");

        var compressedData = await File.ReadAllBytesAsync(tempArchive.Path, cancellationToken);
        var result = new CompressionResult(compressedData, originalSize);

        _logger?.LogInformation("7z.exe compression complete: {CompressedSize} bytes (ratio: {Ratio:P2})",
                result.CompressedSize, result.CompressionRatio);

        return result;
    }

    private async Task Decompress7zExeFileAsync(
        string archivePath, string targetDirectory, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Decompressing {Archive} to {Target} with 7z.exe", archivePath, targetDirectory);

        Directory.CreateDirectory(targetDirectory);

        // -bsp1 streams extraction progress percentages to stdout (parsed in Run7zAsync).
        var arguments = $"x \"{archivePath}\" -o\"{targetDirectory}\" -y -bsp1";

        await Run7zAsync(arguments, progress, cancellationToken);

        _logger?.LogInformation("7z.exe decompression complete.");
    }

    private async Task Run7zAsync(string arguments, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _sevenZipPath!,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        int lastPercent = -1;

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data == null)
                return;
            outputBuilder.AppendLine(e.Data);

            if (progress == null)
                return;
            var match = ProgressPercentPattern.Match(e.Data);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int percent) && percent != lastPercent)
            {
                lastPercent = percent;
                progress.Report(percent);
            }
        };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var error = errorBuilder.ToString();
                throw new CompressionException($"7z failed with exit code {process.ExitCode}. Error: {error}");
            }
        }
        catch (Exception ex) when (ex is not CompressionException and not OperationCanceledException)
        {
            throw new CompressionException($"Failed to run 7z: {ex.Message}", ex);
        }
    }

    #endregion

    #region SharpCompress Implementation

    private async Task<CompressionResult> CompressSharpCompressAsync(string sourcePath, CancellationToken cancellationToken)
    {
        long originalSize = PathSize.GetTotalBytes(sourcePath);
        _logger?.LogInformation("Compressing {Source} ({Size} bytes) with built-in compression", sourcePath, originalSize);

        using var tempArchive = new TempFile();

        try
        {
            using (var stream = File.Create(tempArchive.Path))
            using (var writer = WriterFactory.Open(stream, ArchiveType.SevenZip, new WriterOptions(CompressionType.LZMA)
            {
                LeaveStreamOpen = false,
                ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8 }
            }))
            {
                if (File.Exists(sourcePath))
                {
                    writer.Write(Path.GetFileName(sourcePath), sourcePath);
                }
                else if (Directory.Exists(sourcePath))
                {
                    // Add the folder recursively, keeping its top-level name in the entry paths
                    // ("<folder>/<file>") by using the parent as the base — matches the 7z path.
                    var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(sourcePath))?.FullName;
                    AddDirectoryToArchive(writer, sourcePath, parent ?? sourcePath);
                }
            }

            var compressedData = await File.ReadAllBytesAsync(tempArchive.Path, cancellationToken);
            var result = new CompressionResult(compressedData, originalSize);

            _logger?.LogInformation("Built-in compression complete: {CompressedSize} bytes (ratio: {Ratio:P2})",
                result.CompressedSize, result.CompressionRatio);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CompressionException($"SharpCompress compression failed: {ex.Message}", ex);
        }
    }

    private async Task DecompressSharpCompressFileAsync(string archivePath, string targetDirectory, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Decompressing {Archive} to {Target} with built-in decompression", archivePath, targetDirectory);

        try
        {
            Directory.CreateDirectory(targetDirectory);

            await using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = ReaderFactory.Open(stream);

            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!reader.Entry.IsDirectory)
                {
                    var entryPath = Path.Combine(targetDirectory, reader.Entry.Key);
                    var entryDir = Path.GetDirectoryName(entryPath);

                    if (!string.IsNullOrEmpty(entryDir))
                    {
                        Directory.CreateDirectory(entryDir);
                    }

                    reader.WriteEntryToFile(entryPath, new ExtractionOptions { Overwrite = true });
                }
            }

            _logger?.LogInformation("Built-in decompression complete.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CompressionException($"SharpCompress decompression failed: {ex.Message}", ex);
        }
    }

    private void AddDirectoryToArchive(IWriter writer, string sourceDir, string baseDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var relativePath = Path.GetRelativePath(baseDir, file);
            writer.Write(relativePath, file);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            AddDirectoryToArchive(writer, dir, baseDir);
        }
    }

    #endregion

    #region Helper Methods

    private static string? Find7zExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            var commonPaths = new[]
            {
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var unixPaths = new[] { "/usr/bin/7z", "/usr/local/bin/7z", "/usr/bin/7za" };
            foreach (var path in unixPaths)
            {
                if (File.Exists(path))
                    return path;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            var exeName = OperatingSystem.IsWindows() ? "7z.exe" : "7z";

            foreach (var dir in pathEnv.Split(separator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;

                var candidate = Path.Combine(dir, exeName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"flux_{Guid.NewGuid():N}.7z");

        public void Dispose()
        {
            try { File.Delete(Path); }
            catch { /* Ignore cleanup errors */ }
        }
    }

    #endregion
}
