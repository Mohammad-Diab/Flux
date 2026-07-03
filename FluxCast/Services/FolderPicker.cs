namespace FluxCast.Services;

public interface IFolderPicker
{
   Task<FolderPickerResult> PickAsync(CancellationToken cancellationToken = default);
}

public class FolderPickerResult
{
 public bool IsSuccessful { get; init; }
    public IStorageFolder? Folder { get; init; }

    public static FolderPickerResult Success(IStorageFolder folder) => 
   new() { IsSuccessful = true, Folder = folder };

    public static FolderPickerResult Cancelled() => 
new() { IsSuccessful = false };
}

public interface IStorageFolder
{
    string Path { get; }
    string Name { get; }
}

public class StorageFolder : IStorageFolder
{
    public string Path { get; init; } = string.Empty;
 public string Name => System.IO.Path.GetFileName(Path) ?? string.Empty;
}

public partial class FolderPicker : IFolderPicker
{
    public partial Task<FolderPickerResult> PickAsync(CancellationToken cancellationToken = default);
}
