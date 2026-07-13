using System.IO;
using System.Text.Json;
using FluxCore.Ecc;
using FluxCore.Hashing;

namespace FluxCore.Transfer;

/// <summary>Descriptive fields written into an export's <c>manifest.json</c>.</summary>
public sealed record FrameExportInfo(
    string DisplayName,
    SourceKind SourceKind,
    uint TotalFrames,
    int GridWidthTiles,
    int GridHeightTiles,
    EccLevel EccLevel,
    int ColorCount,
    DateTimeOffset CreatedUtc);

/// <summary>Outcome of a folder export.</summary>
/// <param name="OutputDirectory">The created export subfolder.</param>
/// <param name="FrameCount">Frame PNGs copied.</param>
public sealed record FrameExportResult(string OutputDirectory, int FrameCount);

/// <summary>
/// Copies a cast's rendered frame PNGs to a user-chosen folder for archival or to feed FluxRead's
/// folder-decode. Writes them into a uniquely-named subfolder (never clobbers) alongside a
/// <c>manifest.json</c> carrying the render spec and a per-frame SHA-256 + byte size.
/// </summary>
public static class FrameExporter
{
    /// <summary>The manifest file dropped beside the copied frames.</summary>
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };

    /// <summary>
    /// Copies <c>frame_*.png</c> from <paramref name="framesDirectory"/> into a fresh subfolder of
    /// <paramref name="destinationRoot"/> named from <paramref name="baseName"/> (uniquified on
    /// collision), writes the manifest, and returns the created folder.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The frames directory does not exist.</exception>
    /// <exception cref="InvalidOperationException">No frame PNGs were found to export.</exception>
    public static FrameExportResult ExportToFolder(
        string framesDirectory, string destinationRoot, string baseName,
        FrameExportInfo info, DateTimeOffset exportedUtc)
    {
        ArgumentNullException.ThrowIfNull(framesDirectory);
        ArgumentNullException.ThrowIfNull(destinationRoot);
        ArgumentNullException.ThrowIfNull(baseName);
        ArgumentNullException.ThrowIfNull(info);

        if (!Directory.Exists(framesDirectory))
            throw new DirectoryNotFoundException($"Frames directory not found: {framesDirectory}");

        var frames = Directory
            .GetFiles(framesDirectory, SessionLayout.FrameSearchPattern)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();

        if (frames.Length == 0)
            throw new InvalidOperationException("No frames to export.");

        var outputDir = CreateUniqueSubfolder(destinationRoot, Sanitize(baseName));

        var frameEntries = new List<ExportedFrame>(frames.Length);
        foreach (var source in frames)
        {
            var name = Path.GetFileName(source);
            var bytes = File.ReadAllBytes(source);
            File.WriteAllBytes(Path.Combine(outputDir, name), bytes);
            frameEntries.Add(new ExportedFrame(name, bytes.LongLength, Sha256Helper.ToHexString(Sha256Helper.ComputeHash(bytes))));
        }

        var manifest = new ExportManifest(
            info.DisplayName, info.SourceKind.ToString(),
            info.GridWidthTiles, info.GridHeightTiles, info.EccLevel.ToString(), info.ColorCount,
            info.TotalFrames, info.CreatedUtc, exportedUtc, frameEntries);
        File.WriteAllText(Path.Combine(outputDir, ManifestFileName), JsonSerializer.Serialize(manifest, ManifestJson));

        return new FrameExportResult(outputDir, frames.Length);
    }

    private static string CreateUniqueSubfolder(string root, string name)
    {
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, name);
        for (int n = 2; Directory.Exists(target); n++)
            target = Path.Combine(root, $"{name} ({n})");
        Directory.CreateDirectory(target);
        return target;
    }

    private static string Sanitize(string name)
    {
        var cleaned = string.Concat(name.Select(c => Array.IndexOf(InvalidNameChars, c) >= 0 ? '_' : c)).Trim();
        return cleaned.Length == 0 ? "export" : cleaned;
    }

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    private sealed record ExportManifest(
        string DisplayName,
        string SourceKind,
        int GridWidthTiles,
        int GridHeightTiles,
        string EccLevel,
        int ColorCount,
        uint TotalFrames,
        DateTimeOffset CreatedUtc,
        DateTimeOffset ExportedUtc,
        IReadOnlyList<ExportedFrame> Frames);

    private sealed record ExportedFrame(string File, long Bytes, string Sha256);
}
