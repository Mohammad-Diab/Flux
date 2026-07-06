namespace FluxCore.IO;

/// <summary>Byte counting for a file or a folder's recursive contents.</summary>
internal static class PathSize
{
    internal static long GetTotalBytes(string path)
    {
        if (File.Exists(path))
            return new FileInfo(path).Length;

        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }

        return 0;
    }
}
