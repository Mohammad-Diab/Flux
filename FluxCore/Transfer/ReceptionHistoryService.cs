using System.IO;
using FluxCore.Decoding;
using FluxCore.Framing;

namespace FluxCore.Transfer;

/// <summary>
/// Manages FluxRead's on-disk reception sessions: opens a resumable assembler for an incoming
/// transfer (resuming a matching partial or starting fresh), finalizes a completed reception, and
/// lists or removes past receptions for the history UI. The receiver-side counterpart to the
/// sender's <see cref="FluxEncodeService"/> plus <see cref="CastHistoryService"/>.
/// </summary>
public sealed class ReceptionHistoryService
{
    /// <summary>
    /// Opens a persisting assembler for the transfer described by frame-0 metadata. If a compatible
    /// in-progress session already exists it is resumed (preloading the frames already received);
    /// otherwise a fresh session is created, clearing any stale or completed leftover of the same
    /// signature.
    /// </summary>
    /// <param name="sessionRoot">Directory holding per-reception session folders.</param>
    /// <param name="metadata">Decoded frame-0 metadata for the incoming transfer.</param>
    public PayloadAssembler OpenAssembler(string sessionRoot, MetadataPayload metadata)
    {
        ArgumentNullException.ThrowIfNull(sessionRoot);
        ArgumentNullException.ThrowIfNull(metadata);

        var dir = Path.Combine(sessionRoot, ContentSignature.ToSessionName(metadata.ContentSignature));
        var manifestPath = Path.Combine(dir, ReceptionLayout.ManifestFileName);
        var payloadPath = Path.Combine(dir, ReceptionLayout.PayloadFileName);
        var indexPath = Path.Combine(dir, ReceptionLayout.ReceivedIndexFileName);

        var existing = ReceptionManifest.TryRead(manifestPath);
        bool resume = existing is { Status: ReceptionStatus.InProgress }
            && existing.IsCompatibleWith(metadata)
            && File.Exists(payloadPath)
            && File.Exists(indexPath);

        if (resume)
            return new PayloadAssembler(metadata, payloadPath, indexPath, resume: true);

        // New, incompatible, or previously completed: start clean.
        Directory.CreateDirectory(dir);
        TryDelete(payloadPath);
        TryDelete(indexPath);
        ReceptionManifest.ForMetadata(metadata, DateTimeOffset.UtcNow).Write(manifestPath);
        return new PayloadAssembler(metadata, payloadPath, indexPath, resume: false);
    }

    /// <summary>
    /// Marks a reception complete: records where the verified output was saved and deletes the now
    /// redundant received buffer, keeping only the manifest as a history record.
    /// </summary>
    /// <param name="sessionDirectory">The reception's session folder.</param>
    /// <param name="savedPath">Where the extracted output was saved.</param>
    public void MarkComplete(string sessionDirectory, string savedPath)
    {
        ArgumentNullException.ThrowIfNull(sessionDirectory);

        var manifestPath = Path.Combine(sessionDirectory, ReceptionLayout.ManifestFileName);
        if (ReceptionManifest.TryRead(manifestPath) is not { } manifest)
            return;

        (manifest with
        {
            Status = ReceptionStatus.Complete,
            SavedPath = savedPath,
            CompletedUtc = DateTimeOffset.UtcNow,
        }).Write(manifestPath);

        TryDelete(Path.Combine(sessionDirectory, ReceptionLayout.PayloadFileName));
        TryDelete(Path.Combine(sessionDirectory, ReceptionLayout.ReceivedIndexFileName));
    }

    /// <summary>Lists past receptions under the session root, most recent activity first.</summary>
    /// <param name="sessionRoot">Directory holding per-reception session folders.</param>
    public IReadOnlyList<ReceptionEntry> List(string sessionRoot)
    {
        if (!Directory.Exists(sessionRoot))
            return [];

        var entries = new List<ReceptionEntry>();
        foreach (var dir in Directory.EnumerateDirectories(sessionRoot))
        {
            var manifest = ReceptionManifest.TryRead(Path.Combine(dir, ReceptionLayout.ManifestFileName));
            if (manifest is null)
                continue;

            bool complete = manifest.Status == ReceptionStatus.Complete;
            uint expected = manifest.TotalFrames == 0 ? 0 : manifest.TotalFrames - 1;
            int received = complete
                ? (int)expected
                : CountReceivedIds(Path.Combine(dir, ReceptionLayout.ReceivedIndexFileName));
            var created = manifest.CreatedUtc
                ?? new DateTimeOffset(Directory.GetCreationTimeUtc(dir), TimeSpan.Zero);

            entries.Add(new ReceptionEntry(
                dir, manifest.OriginalName, manifest.PayloadType, manifest.PayloadLength,
                manifest.OriginalLength, manifest.TotalFrames, received, created,
                manifest.CompletedUtc, complete, manifest.SavedPath));
        }

        return entries.OrderByDescending(e => e.CompletedUtc ?? e.CreatedUtc).ToList();
    }

    /// <summary>Deletes a reception session folder and everything in it.</summary>
    /// <param name="sessionDirectory">The session folder to remove.</param>
    public void Delete(string sessionDirectory)
    {
        if (Directory.Exists(sessionDirectory))
            Directory.Delete(sessionDirectory, recursive: true);
    }

    private static int CountReceivedIds(string indexPath) =>
        File.Exists(indexPath) ? (int)(new FileInfo(indexPath).Length / sizeof(uint)) : 0;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* best-effort cleanup */
        }
    }
}
