using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Readers;
using System.Diagnostics;
using System.Text;

namespace FluxCore.Compression;

/// <summary>
/// Provides compression and decompression using external 7z utility (preferred) or SharpCompress (fallback).
/// Also supports raw (uncompressed) passthrough mode.
/// </summary>
public sealed class CompressionService
{
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

    /// <summary>
    /// Initializes a new instance of the <see cref="CompressionService"/> class.
    /// </summary>
    /// <param name="sevenZipPath">Path to 7z.exe. If null, searches PATH.</param>
    /// <param name="prefer7zExe">If true, prefers 7z.exe when available (default: true).</param>
    /// <param name="logger">Optional logger.</param>
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

    /// <summary>
    /// Compresses a file or folder using 7z (preferred) or built-in compression (fallback).
    /// </summary>
    /// <param name="sourcePath">Path to file or folder to compress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compression result containing compressed data and metadata.</returns>
    /// <exception cref="CompressionException">Thrown when compression fails.</exception>
    public async Task<CompressionResult> CompressAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            throw new FileNotFoundException($"Source not found: {sourcePath}");

        return Using7zExe
      ? await Compress7zExeAsync(sourcePath, cancellationToken)
            : await CompressSharpCompressAsync(sourcePath, cancellationToken);
    }

    /// <summary>
    /// Decompresses a 7z archive to a target directory.
    /// </summary>
    /// <param name="compressedData">Compressed 7z data.</param>
    /// <param name="targetDirectory">Target directory for extraction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="CompressionException">Thrown when decompression fails.</exception>
    public async Task DecompressAsync(byte[] compressedData, string targetDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compressedData);
        ArgumentNullException.ThrowIfNull(targetDirectory);

        // Try 7z.exe first if available, fallback to SharpCompress
        if (Using7zExe)
        {
            try
            {
                await Decompress7zExeAsync(compressedData, targetDirectory, cancellationToken);
                return;
            }
            catch (CompressionException ex)
            {
                _logger?.LogWarning(ex, "7z.exe decompression failed, falling back to built-in decompression.");
            }
        }

        await DecompressSharpCompressAsync(compressedData, targetDirectory, cancellationToken);
    }

    /// <summary>
    /// Creates a raw (uncompressed) passthrough result.
    /// </summary>
    /// <param name="data">Data to pass through.</param>
    /// <returns>Compression result with identical input and output.</returns>
    public CompressionResult CreateRaw(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new CompressionResult(data, data.Length);
    }

    /// <summary>
    /// Checks if 7z is available on the system.
    /// </summary>
    /// <returns>True if 7z.exe is found; false otherwise.</returns>
    public bool Is7zAvailable() => _has7zExe;

    #region 7z.exe Implementation

    private async Task<CompressionResult> Compress7zExeAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var tempArchive = Path.Combine(Path.GetTempPath(), $"flux_{Guid.NewGuid():N}.7z");

        try
        {
            long originalSize = GetSize(sourcePath);
            _logger?.LogInformation("Compressing {Source} ({Size} bytes) with 7z.exe", sourcePath, originalSize);

            // Use maximum compression: -mx=9, LZMA2, solid archive.
            // For directories, archive the contents (dir\*) so entries are relative to
            // the directory itself, matching the SharpCompress fallback.
            var source = Directory.Exists(sourcePath) ? Path.Combine(sourcePath, "*") : sourcePath;
            var arguments = $"a -t7z -mx=9 -m0=lzma2 -ms=on \"{tempArchive}\" \"{source}\"";

            await Run7zAsync(arguments, cancellationToken);

            if (!File.Exists(tempArchive))
                throw new CompressionException("7z compression produced no output archive.");

            var compressedData = await File.ReadAllBytesAsync(tempArchive, cancellationToken);
            var result = new CompressionResult(compressedData, originalSize);

            _logger?.LogInformation("7z.exe compression complete: {CompressedSize} bytes (ratio: {Ratio:P2})",
                    result.CompressedSize, result.CompressionRatio);

            return result;
        }
        finally
        {
            if (File.Exists(tempArchive))
            {
                try { File.Delete(tempArchive); }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }

    private async Task Decompress7zExeAsync(byte[] compressedData, string targetDirectory, CancellationToken cancellationToken)
    {
        var tempArchive = Path.Combine(Path.GetTempPath(), $"flux_{Guid.NewGuid():N}.7z");

        try
        {
            await File.WriteAllBytesAsync(tempArchive, compressedData, cancellationToken);

            _logger?.LogInformation("Decompressing {Size} bytes to {Target} with 7z.exe", compressedData.Length, targetDirectory);

            Directory.CreateDirectory(targetDirectory);

            var arguments = $"x \"{tempArchive}\" -o\"{targetDirectory}\" -y";

            await Run7zAsync(arguments, cancellationToken);

            _logger?.LogInformation("7z.exe decompression complete.");
        }
        finally
        {
            if (File.Exists(tempArchive))
            {
                try { File.Delete(tempArchive); }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }

    private async Task Run7zAsync(string arguments, CancellationToken cancellationToken)
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

        process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
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
        long originalSize = GetSize(sourcePath);
        _logger?.LogInformation("Compressing {Source} ({Size} bytes) with built-in compression", sourcePath, originalSize);

        var tempArchive = Path.Combine(Path.GetTempPath(), $"flux_{Guid.NewGuid():N}.7z");

        try
        {
            using (var stream = File.Create(tempArchive))
            using (var writer = WriterFactory.Open(stream, ArchiveType.SevenZip, new WriterOptions(CompressionType.LZMA)
            {
                LeaveStreamOpen = false,
                ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8 }
            }))
            {
                if (File.Exists(sourcePath))
                {
                    // Single file
                    writer.Write(Path.GetFileName(sourcePath), sourcePath);
                }
                else if (Directory.Exists(sourcePath))
                {
                    // Directory - add all files recursively
                    AddDirectoryToArchive(writer, sourcePath, sourcePath);
                }
            }

            var compressedData = await File.ReadAllBytesAsync(tempArchive, cancellationToken);
            var result = new CompressionResult(compressedData, originalSize);

            _logger?.LogInformation("Built-in compression complete: {CompressedSize} bytes (ratio: {Ratio:P2})",
         result.CompressedSize, result.CompressionRatio);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CompressionException($"SharpCompress compression failed: {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(tempArchive))
            {
                try { File.Delete(tempArchive); }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }

    private async Task DecompressSharpCompressAsync(byte[] compressedData, string targetDirectory, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Decompressing {Size} bytes to {Target} with built-in decompression", compressedData.Length, targetDirectory);

        try
        {
            Directory.CreateDirectory(targetDirectory);

            using var stream = new MemoryStream(compressedData);
            using var reader = ReaderFactory.Open(stream);

            while (reader.MoveToNextEntry())
            {
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
            // Windows-specific paths
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
            // Unix-specific paths
            var unixPaths = new[] { "/usr/bin/7z", "/usr/local/bin/7z", "/usr/bin/7za" };
            foreach (var path in unixPaths)
            {
                if (File.Exists(path))
                    return path;
            }
        }

        // Search PATH environment variable
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

        return null; // Not found
    }

    private static long GetSize(string path)
    {
        if (File.Exists(path))
            return new FileInfo(path).Length;

        if (Directory.Exists(path))
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
    .Sum(f => new FileInfo(f).Length);
        }

        return 0;
    }

    #endregion
}
