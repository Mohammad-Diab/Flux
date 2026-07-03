using FluxCore.Framing;
using Microsoft.Extensions.Logging;

namespace FluxCast.Services;

/// <summary>
/// Service for parsing and managing encode metadata files.
/// </summary>
public class MetadataFileService
{
    private readonly ILogger<MetadataFileService>? _logger;

    public MetadataFileService(ILogger<MetadataFileService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses an encode_meta.txt file.
  /// </summary>
    public EncodeMetadata? ParseMetadataFile(string metadataFilePath)
    {
        try
        {
          if (!File.Exists(metadataFilePath))
       {
 _logger?.LogWarning("Metadata file not found: {Path}", metadataFilePath);
 return null;
       }

     var lines = File.ReadAllLines(metadataFilePath);
    var metadata = new EncodeMetadata { MetadataFilePath = metadataFilePath };

            foreach (var line in lines)
            {
      if (string.IsNullOrWhiteSpace(line) || !line.Contains('='))
       continue;

                var parts = line.Split('=', 2);
       var key = parts[0].Trim();
     var value = parts[1].Trim();

    switch (key)
          {
     case "Version":
         metadata.Version = int.Parse(value);
    break;
case "EncodingCode":
       metadata.EncodingCode = byte.Parse(value);
        break;
    case "TileSize":
metadata.TileSize = (TileSize)int.Parse(value);
        break;
          case "ECCPer16":
      metadata.EccPer16 = int.Parse(value);
     break;
     case "SeparatorEvery":
       metadata.SeparatorEvery = int.Parse(value);
      break;
    case "FrameWidthPx":
             metadata.FrameWidthPx = int.Parse(value);
 break;
    case "FrameHeightPx":
               metadata.FrameHeightPx = int.Parse(value);
 break;
     case "TotalFrames":
metadata.TotalFrames = int.Parse(value);
               break;
           case "FirstUsefulFrameIndex":
 metadata.FirstUsefulFrameIndex = int.Parse(value);
             break;
         case "LastFailedFrameIndex":
    metadata.LastFailedFrameIndex = int.Parse(value);
      break;
     case "PayloadType":
           metadata.PayloadType = value;
              break;
           case "OriginalName":
          metadata.OriginalName = value;
                  break;
       case "ColorMap":
  metadata.ColorMap = value;
           break;
   }
      }

            // Infer frames directory from metadata file path
            metadata.FramesDirectory = Path.GetDirectoryName(metadataFilePath) ?? string.Empty;

            _logger?.LogInformation("Parsed metadata: {TotalFrames} frames, TileSize={TileSize}, ECC={ECC}",
 metadata.TotalFrames, metadata.TileSize, metadata.EccPer16);

    return metadata;
     }
        catch (Exception ex)
        {
      _logger?.LogError(ex, "Failed to parse metadata file: {Path}", metadataFilePath);
     return null;
        }
    }

    /// <summary>
    /// Validates that all frame files exist.
    /// </summary>
    public FrameValidationResult ValidateFrames(EncodeMetadata metadata)
    {
 var result = new FrameValidationResult();

  for (int i = 0; i < metadata.TotalFrames; i++)
  {
            var framePath = Path.Combine(metadata.FramesDirectory, $"frame_{i:D6}.png");
            
            if (File.Exists(framePath))
      {
         result.ExistingFrames.Add(i);
   }
      else
            {
            result.MissingFrames.Add(i);
  }
   }

        result.IsComplete = result.MissingFrames.Count == 0;

        _logger?.LogInformation("Frame validation: {Existing}/{Total} frames exist, {Missing} missing",
            result.ExistingFrames.Count, metadata.TotalFrames, result.MissingFrames.Count);

        return result;
    }

    /// <summary>
    /// Loads frame images from disk.
    /// </summary>
    public async Task<List<byte[]>> LoadFramesAsync(EncodeMetadata metadata, IProgress<int>? progress = null)
    {
        var frames = new List<byte[]>();

   for (int i = 0; i < metadata.TotalFrames; i++)
        {
            var framePath = Path.Combine(metadata.FramesDirectory, $"frame_{i:D6}.png");
     
     if (File.Exists(framePath))
            {
                var frameData = await File.ReadAllBytesAsync(framePath);
    frames.Add(frameData);
            }
       else
    {
           _logger?.LogWarning("Frame {Index} not found: {Path}", i, framePath);
       // Add empty placeholder or skip
  frames.Add(Array.Empty<byte>());
  }

       progress?.Report(i + 1);
      }

        return frames;
    }
}

/// <summary>
/// Parsed encode metadata.
/// </summary>
public class EncodeMetadata
{
    public string MetadataFilePath { get; set; } = string.Empty;
    public string FramesDirectory { get; set; } = string.Empty;
    public int Version { get; set; }
    public byte EncodingCode { get; set; }
    public TileSize TileSize { get; set; }
    public int EccPer16 { get; set; }
    public int SeparatorEvery { get; set; }
    public int FrameWidthPx { get; set; }
    public int FrameHeightPx { get; set; }
  public int TotalFrames { get; set; }
public int FirstUsefulFrameIndex { get; set; }
    public int LastFailedFrameIndex { get; set; }
    public string PayloadType { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string ColorMap { get; set; } = string.Empty;
}

/// <summary>
/// Result of frame validation.
/// </summary>
public class FrameValidationResult
{
    public bool IsComplete { get; set; }
    public List<int> ExistingFrames { get; set; } = new();
    public List<int> MissingFrames { get; set; } = new();
}
