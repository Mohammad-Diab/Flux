using System.IO;
using FluxCore.Hashing;

namespace FluxCore.Transfer;

/// <summary>Lists, opens, and removes past encode sessions for FluxCast's history.</summary>
public sealed class CastHistoryService
{
    /// <summary>Lists past casts under the session root, newest first.</summary>
    /// <param name="sessionRoot">Directory holding per-cast session folders.</param>
    public IReadOnlyList<CastHistoryEntry> List(string sessionRoot)
    {
        if (!Directory.Exists(sessionRoot))
            return [];

        var entries = new List<CastHistoryEntry>();
        foreach (var dir in Directory.EnumerateDirectories(sessionRoot))
        {
            var manifest = SessionManifest.TryRead(Path.Combine(dir, SessionLayout.ManifestFileName));
            if (manifest is null)
                continue;

            var framesDir = Path.Combine(dir, SessionLayout.FramesFolderName);
            int onDisk = Directory.Exists(framesDir)
                ? Directory.EnumerateFiles(framesDir, SessionLayout.FrameSearchPattern).Count()
                : 0;
            uint total = manifest.TotalFrames != 0 ? manifest.TotalFrames : (uint)onDisk;
            bool complete = onDisk > 0 && onDisk >= total;
            var created = manifest.CreatedUtc
                ?? new DateTimeOffset(Directory.GetCreationTimeUtc(dir), TimeSpan.Zero);
            var name = !string.IsNullOrEmpty(manifest.DisplayName)
                ? manifest.DisplayName!
                : Path.GetFileName(dir);

            entries.Add(new CastHistoryEntry(
                dir, framesDir, name, manifest.SourcePath, manifest.SourceKind,
                total, manifest.PayloadLength, created, complete));
        }

        return entries.OrderByDescending(e => e.CreatedUtc).ToList();
    }

    /// <summary>Deletes a session folder and everything in it.</summary>
    /// <param name="sessionDirectory">The session folder to remove.</param>
    public void Delete(string sessionDirectory)
    {
        if (Directory.Exists(sessionDirectory))
            Directory.Delete(sessionDirectory, recursive: true);
    }

    /// <summary>Builds a session result for re-presenting an existing cast from its frames on disk.</summary>
    /// <param name="sessionDirectory">The session folder to open.</param>
    public EncodeSessionResult OpenForPresenting(string sessionDirectory)
    {
        var manifest = SessionManifest.TryRead(Path.Combine(sessionDirectory, SessionLayout.ManifestFileName))
            ?? throw new InvalidOperationException($"No session manifest in '{sessionDirectory}'.");
        var framesDir = Path.Combine(sessionDirectory, SessionLayout.FramesFolderName);
        uint total = manifest.TotalFrames != 0
            ? manifest.TotalFrames
            : (uint)Directory.EnumerateFiles(framesDir, SessionLayout.FrameSearchPattern).Count();

        return new EncodeSessionResult(
            sessionDirectory, framesDir, total, manifest.PayloadLength,
            Sha256Helper.FromHexString(manifest.SignatureHex), PayloadReused: true, FramesRendered: 0);
    }
}
