using FluxCore.Compression;
using Microsoft.Extensions.Logging;

namespace FluxCast.Services;

/// <summary>
/// Service for validating and processing input files/folders before encoding.
/// </summary>
public class InputValidationService
{
    private readonly ILogger<InputValidationService>? _logger;
    private readonly CompressionService _compressionService;

    // Limits
    private const long MaxFileSizeBytes = 10L * 1024 * 1024 * 1024; // 10 GB
 private const long MaxFolderSizeBytes = 50L * 1024 * 1024 * 1024; // 50 GB
    private const int MaxFileCount = 100000;

  public InputValidationService(CompressionService compressionService, ILogger<InputValidationService>? logger = null)
    {
        _compressionService = compressionService;
        _logger = logger;
    }

    /// <summary>
    /// Validates a file or folder for encoding.
    /// </summary>
    /// <param name="path">Path to file or folder.</param>
    /// <param name="isFolder">True if path is a folder.</param>
    /// <param name="requireCompression">True if compression is required.</param>
    /// <returns>Validation result.</returns>
 public async Task<InputValidationResult> ValidateInputAsync(string path, bool isFolder, bool requireCompression)
    {
        _logger?.LogInformation("Validating input: {Path} (IsFolder: {IsFolder})", path, isFolder);

        // Check existence
        if (isFolder)
        {
 if (!Directory.Exists(path))
             return InputValidationResult.Failure($"Folder not found: {path}");
        }
   else
   {
        if (!File.Exists(path))
                return InputValidationResult.Failure($"File not found: {path}");
        }

    // Check 7z availability if compression required
        if (requireCompression && !_compressionService.Is7zAvailable())
        {
   var warning = "7z.exe not found. Built-in compression will be used (slower).\n" +
               "For best performance, install 7-Zip from: https://www.7-zip.org";
            _logger?.LogWarning(warning);
            
            return InputValidationResult.SuccessWithWarning(warning);
        }

        // Validate file
        if (!isFolder)
     {
       return await ValidateFileAsync(path);
      }

    // Validate folder
        return await ValidateFolderAsync(path);
    }

    private async Task<InputValidationResult> ValidateFileAsync(string filePath)
    {
        var fileInfo = new FileInfo(filePath);

   // Check file size
        if (fileInfo.Length == 0)
            return InputValidationResult.Failure("File is empty.");

        if (fileInfo.Length > MaxFileSizeBytes)
        {
            var sizeMB = fileInfo.Length / 1024.0 / 1024.0;
         var maxMB = MaxFileSizeBytes / 1024.0 / 1024.0;
            return InputValidationResult.Failure(
         $"File is too large: {sizeMB:F2} MB (max: {maxMB:F0} MB).");
  }

   // Check file accessibility
        try
        {
    using var stream = File.OpenRead(filePath);
       // File is accessible
        }
   catch (UnauthorizedAccessException)
        {
            return InputValidationResult.Failure("Access denied. Check file permissions.");
   }
        catch (IOException ex)
    {
     return InputValidationResult.Failure($"Cannot access file: {ex.Message}");
        }

    _logger?.LogInformation("File validation passed: {Size} bytes", fileInfo.Length);
        return InputValidationResult.Success($"File: {fileInfo.Length / 1024.0:F2} KB");
    }

    private async Task<InputValidationResult> ValidateFolderAsync(string folderPath)
    {
      try
    {
            var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);

            // Check file count
          if (files.Length == 0)
         return InputValidationResult.Failure("Folder is empty.");

   if (files.Length > MaxFileCount)
                return InputValidationResult.Failure(
           $"Too many files: {files.Length} (max: {MaxFileCount}).");

          // Calculate total size
         long totalSize = 0;
  foreach (var file in files)
            {
      var fileInfo = new FileInfo(file);
  totalSize += fileInfo.Length;

    if (totalSize > MaxFolderSizeBytes)
     {
        var sizeMB = totalSize / 1024.0 / 1024.0;
         var maxMB = MaxFolderSizeBytes / 1024.0 / 1024.0;
               return InputValidationResult.Failure(
        $"Folder is too large: {sizeMB:F2} MB (max: {maxMB:F0} MB).");
     }
            }

            _logger?.LogInformation("Folder validation passed: {FileCount} files, {Size} bytes", 
          files.Length, totalSize);

            return InputValidationResult.Success(
  $"Folder: {files.Length} files, {totalSize / 1024.0 / 1024.0:F2} MB");
        }
        catch (UnauthorizedAccessException)
        {
            return InputValidationResult.Failure("Access denied. Check folder permissions.");
        }
        catch (Exception ex)
     {
      return InputValidationResult.Failure($"Error accessing folder: {ex.Message}");
    }
    }

    /// <summary>
    /// Estimates the number of frames that will be generated.
    /// </summary>
    public async Task<int> EstimateFrameCountAsync(string path, bool isFolder, bool enableCompression, int tileSize, int eccLevel)
    {
        try
        {
// Get input size
     long inputSize = isFolder
                ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
  : new FileInfo(path).Length;

   // Estimate compressed size (very rough)
            long estimatedPayloadSize = inputSize;
 if (enableCompression || isFolder)
        {
   // Typical compression ratios: text=0.3, mixed=0.5, images=0.9
          estimatedPayloadSize = (long)(inputSize * 0.5);
  }

    // Calculate frame capacity
 int frameWidthTiles = CalculateTileCapacity(1920, tileSize);
        int frameHeightTiles = CalculateTileCapacity(1080, tileSize);
  int tilesPerFrame = frameWidthTiles * frameHeightTiles;

       // Account for ECC overhead
            int dataBlocks = (tilesPerFrame + 15) / 16;
        int eccTiles = dataBlocks * eccLevel;
            int dataCapacityPerFrame = tilesPerFrame - eccTiles;

       // Estimate frame count
   int estimatedFrames = (int)Math.Ceiling((double)estimatedPayloadSize / dataCapacityPerFrame) + 1; // +1 for metadata

 return estimatedFrames;
        }
        catch
        {
            return 0; // Unknown
        }
 }

    private int CalculateTileCapacity(int dimension, int tileSize)
    {
        int margin = Math.Max(1, tileSize / 4);
    int separator = Math.Max(2, tileSize / 2);
        int available = dimension - 2 * margin;

        int maxTiles = available / tileSize;
        int separatorCount = maxTiles / 8;
        int separatorSpace = separatorCount * separator;
        int adjustedAvailable = available - separatorSpace;

        return Math.Max(0, adjustedAvailable / tileSize);
    }
}

/// <summary>
/// Result of input validation.
/// </summary>
public class InputValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public string? WarningMessage { get; init; }
    public string? InfoMessage { get; init; }

    public static InputValidationResult Success(string? info = null) =>
        new() { IsValid = true, InfoMessage = info };

    public static InputValidationResult SuccessWithWarning(string warning) =>
   new() { IsValid = true, WarningMessage = warning };

    public static InputValidationResult Failure(string error) =>
        new() { IsValid = false, ErrorMessage = error };
}
