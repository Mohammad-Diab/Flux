namespace FluxCore.Transfer;

/// <summary>Whether a cast's source was a single file or a folder.</summary>
public enum SourceKind
{
    /// <summary>A single file.</summary>
    File,

    /// <summary>A folder (always compressed).</summary>
    Folder,
}
