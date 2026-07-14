using System.IO;
using FluxCore.Hashing;

namespace FluxCore.Transfer;

/// <summary>Lists, opens, and removes past encode sessions for FluxCast's history.</summary>
public sealed class CastHistoryService
{
    /// <summary>Lists past casts (one per render variant) under the session root, newest first.</summary>
    /// <param name="sessionRoot">Directory holding per-payload session folders.</param>
    public IReadOnlyList<CastHistoryEntry> List(string sessionRoot)
    {
        if (!Directory.Exists(sessionRoot))
            return [];

        var entries = new List<CastHistoryEntry>();
        foreach (var payloadDir in Directory.EnumerateDirectories(sessionRoot))
        {
            var payload = PayloadManifest.TryRead(Path.Combine(payloadDir, SessionLayout.PayloadManifestFileName));
            if (payload is null)
                continue;

            var rendersRoot = Path.Combine(payloadDir, SessionLayout.RendersFolderName);
            if (!Directory.Exists(rendersRoot))
                continue;

            var name = !string.IsNullOrEmpty(payload.DisplayName)
                ? payload.DisplayName!
                : Path.GetFileName(payloadDir);

            foreach (var renderDir in Directory.EnumerateDirectories(rendersRoot))
            {
                var render = RenderManifest.TryRead(Path.Combine(renderDir, SessionLayout.RenderManifestFileName));
                if (render is null)
                    continue;

                var framesDir = Path.Combine(renderDir, SessionLayout.FramesFolderName);
                int onDisk = Directory.Exists(framesDir)
                    ? Directory.EnumerateFiles(framesDir, SessionLayout.FrameSearchPattern).Count()
                    : 0;
                uint total = render.TotalFrames != 0 ? render.TotalFrames : (uint)onDisk;
                bool complete = onDisk > 0 && onDisk >= total;
                var created = render.CreatedUtc
                    ?? payload.CreatedUtc
                    ?? new DateTimeOffset(Directory.GetCreationTimeUtc(renderDir), TimeSpan.Zero);

                entries.Add(new CastHistoryEntry(
                    renderDir, framesDir, name, payload.SourcePath, payload.SourceKind,
                    total, payload.PayloadLength, created, complete,
                    render.EccLevel, render.GridWidthTiles, render.GridHeightTiles, render.ColorCount, render.PaletteKind));
            }
        }

        return entries.OrderByDescending(e => e.CreatedUtc).ToList();
    }

    /// <summary>
    /// Deletes a render variant. When it was the payload's last variant, the shared payload folder
    /// is removed too so no orphaned payload.dat is left behind.
    /// </summary>
    /// <param name="renderDirectory">The render-variant folder to remove.</param>
    public void Delete(string renderDirectory)
    {
        if (!Directory.Exists(renderDirectory))
            return;

        var rendersRoot = Path.GetDirectoryName(renderDirectory);
        Directory.Delete(renderDirectory, recursive: true);

        if (rendersRoot is not null && Directory.Exists(rendersRoot) &&
            !Directory.EnumerateFileSystemEntries(rendersRoot).Any())
        {
            var payloadDir = Path.GetDirectoryName(rendersRoot);
            if (payloadDir is not null && Directory.Exists(payloadDir))
                Directory.Delete(payloadDir, recursive: true);
        }
    }

    /// <summary>Builds a session result for re-presenting an existing render variant from its frames on disk.</summary>
    /// <param name="renderDirectory">The render-variant folder to open.</param>
    public EncodeSessionResult OpenForPresenting(string renderDirectory)
    {
        var render = RenderManifest.TryRead(Path.Combine(renderDirectory, SessionLayout.RenderManifestFileName))
            ?? throw new InvalidOperationException($"No render manifest in '{renderDirectory}'.");
        var payloadDirectory = Path.GetDirectoryName(Path.GetDirectoryName(renderDirectory))!;
        var payload = PayloadManifest.TryRead(Path.Combine(payloadDirectory, SessionLayout.PayloadManifestFileName));
        var framesDir = Path.Combine(renderDirectory, SessionLayout.FramesFolderName);
        uint total = render.TotalFrames != 0
            ? render.TotalFrames
            : (uint)Directory.EnumerateFiles(framesDir, SessionLayout.FrameSearchPattern).Count();

        return new EncodeSessionResult(
            renderDirectory, payloadDirectory, framesDir, total,
            payload?.PayloadLength ?? 0,
            Sha256Helper.FromHexString(render.CombinedSignatureHex),
            PayloadReused: true, FramesRendered: 0);
    }
}
