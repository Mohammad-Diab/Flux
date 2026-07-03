using FluxRead.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FluxRead.Services;

/// <summary>
/// Manages decoder session persistence and resume functionality.
/// </summary>
public class DecoderSessionManager
{
    private readonly ILogger<DecoderSessionManager>? _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
      WriteIndented = true
    };

    public DecoderSessionManager(ILogger<DecoderSessionManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Saves decoder session progress to JSON file.
    /// </summary>
    public async Task SaveProgressAsync(DecodeSession session, string outputFolder)
    {
        try
    {
            var progressPath = Path.Combine(outputFolder, "decoder_progress.json");
          
        var progressData = new DecoderProgressData
            {
 Version = session.Version,
      SessionId = session.SessionId,
                InputMode = session.InputMode.ToString(),
         SourceFolder = session.SourceFolder ?? string.Empty,
 OutputPath = session.OutputPath ?? string.Empty,
 Progress = new ProgressData
            {
       TotalFrames = session.Progress.TotalFrames,
    DecodedFrames = session.Progress.DecodedFrames,
             FailedFrames = session.Progress.FailedFrames,
         EccCorrections = session.Progress.EccCorrections,
          CrcFailures = session.Progress.CrcFailures,
             StartTime = session.Progress.StartTime,
        EndTime = session.Progress.EndTime
        },
   Metadata = session.Metadata != null ? new MetadataInfo
       {
      OriginalName = session.Metadata.OriginalName,
       OriginalLength = session.Metadata.OriginalLength,
              TileSize = session.Metadata.TileSize,
 EccLevel = session.Metadata.EccLevel,
        PayloadType = session.Metadata.PayloadType
      } : null,
              Errors = session.Errors,
       Logs = session.Logs
            };

     var json = JsonSerializer.Serialize(progressData, JsonOptions);
          await File.WriteAllTextAsync(progressPath, json);

            _logger?.LogInformation("Saved decoder progress to {Path}", progressPath);
        }
        catch (Exception ex)
        {
   _logger?.LogError(ex, "Failed to save decoder progress");
        }
    }

    /// <summary>
    /// Loads decoder session progress from JSON file.
    /// </summary>
    public async Task<DecodeSession?> LoadProgressAsync(string progressFilePath)
    {
        try
        {
            if (!File.Exists(progressFilePath))
    {
       _logger?.LogWarning("Progress file not found: {Path}", progressFilePath);
         return null;
    }

            var json = await File.ReadAllTextAsync(progressFilePath);
         var progressData = JsonSerializer.Deserialize<DecoderProgressData>(json, JsonOptions);

    if (progressData == null)
    return null;

    var session = new DecodeSession
        {
      Version = progressData.Version,
           SessionId = progressData.SessionId,
       InputMode = Enum.Parse<InputMode>(progressData.InputMode),
          SourceFolder = progressData.SourceFolder,
      OutputPath = progressData.OutputPath,
  Progress = new DecodeProgress
  {
        TotalFrames = progressData.Progress.TotalFrames,
     DecodedFrames = progressData.Progress.DecodedFrames,
       FailedFrames = progressData.Progress.FailedFrames,
   EccCorrections = progressData.Progress.EccCorrections,
         CrcFailures = progressData.Progress.CrcFailures,
                StartTime = progressData.Progress.StartTime,
              EndTime = progressData.Progress.EndTime
                },
    Metadata = progressData.Metadata != null ? new DecodedMetadata
          {
             OriginalName = progressData.Metadata.OriginalName,
     OriginalLength = progressData.Metadata.OriginalLength,
        TileSize = progressData.Metadata.TileSize,
 EccLevel = progressData.Metadata.EccLevel,
        PayloadType = progressData.Metadata.PayloadType,
        Sha256 = Array.Empty<byte>() // Not stored in progress file
      } : null,
             Errors = progressData.Errors,
          Logs = progressData.Logs
      };

   _logger?.LogInformation("Loaded decoder session {SessionId}", session.SessionId);
   return session;
        }
        catch (Exception ex)
        {
 _logger?.LogError(ex, "Failed to load decoder progress from {Path}", progressFilePath);
  return null;
        }
    }

    /// <summary>
    /// Gets list of incomplete decoder sessions.
    /// </summary>
    public List<string> GetIncompleteSessionFiles(string searchFolder)
  {
     var incompleteSessions = new List<string>();

  try
        {
    if (!Directory.Exists(searchFolder))
       return incompleteSessions;

    var progressFiles = Directory.GetFiles(searchFolder, "decoder_progress.json", SearchOption.AllDirectories);

       foreach (var file in progressFiles)
{
         try
        {
            var json = File.ReadAllText(file);
                 var progressData = JsonSerializer.Deserialize<DecoderProgressData>(json, JsonOptions);

             // Check if session is incomplete
       if (progressData?.Progress.EndTime == null || progressData.Progress.FailedFrames > 0)
        {
            incompleteSessions.Add(file);
   }
            }
   catch
      {
      // Skip invalid files
           }
  }

 _logger?.LogInformation("Found {Count} incomplete decoder sessions", incompleteSessions.Count);
      }
        catch (Exception ex)
{
            _logger?.LogError(ex, "Failed to search for incomplete sessions");
        }

  return incompleteSessions;
    }

    /// <summary>
    /// Exports logs to text file.
    /// </summary>
    public async Task ExportLogsAsync(DecodeSession session, string outputPath)
    {
        try
        {
            var logPath = Path.Combine(outputPath, $"decoder_logs_{session.SessionId}.txt");
         
   var logContent = new System.Text.StringBuilder();
            logContent.AppendLine($"=== FluxRead Decoder Logs ===");
   logContent.AppendLine($"Session ID: {session.SessionId}");
        logContent.AppendLine($"Input Mode: {session.InputMode}");
 logContent.AppendLine($"Source: {session.SourceFolder}");
  logContent.AppendLine($"Start Time: {session.Progress.StartTime:yyyy-MM-dd HH:mm:ss}");
      logContent.AppendLine($"End Time: {session.Progress.EndTime:yyyy-MM-dd HH:mm:ss}");
            logContent.AppendLine();
  logContent.AppendLine($"=== Summary ===");
         logContent.AppendLine($"Total Frames: {session.Progress.TotalFrames}");
   logContent.AppendLine($"Decoded Frames: {session.Progress.DecodedFrames}");
            logContent.AppendLine($"Failed Frames: {session.Progress.FailedFrames}");
      logContent.AppendLine($"ECC Corrections: {session.Progress.EccCorrections}");
            logContent.AppendLine($"CRC Failures: {session.Progress.CrcFailures}");
      logContent.AppendLine();
            logContent.AppendLine($"=== Detailed Logs ===");
            
        foreach (var log in session.Logs)
     {
        logContent.AppendLine(log);
            }

  if (session.Errors.Any())
    {
      logContent.AppendLine();
   logContent.AppendLine($"=== Errors ===");
        foreach (var error in session.Errors)
        {
      logContent.AppendLine(error);
          }
 }

          await File.WriteAllTextAsync(logPath, logContent.ToString());

            _logger?.LogInformation("Exported logs to {Path}", logPath);
        }
        catch (Exception ex)
        {
    _logger?.LogError(ex, "Failed to export logs");
        }
    }
}

// JSON serialization classes
public class DecoderProgressData
{
    public string Version { get; set; } = "1.0";
    public string SessionId { get; set; } = string.Empty;
    public string InputMode { get; set; } = string.Empty;
    public string SourceFolder { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public ProgressData Progress { get; set; } = new();
    public MetadataInfo? Metadata { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Logs { get; set; } = new();
}

public class ProgressData
{
    public int TotalFrames { get; set; }
    public int DecodedFrames { get; set; }
    public int FailedFrames { get; set; }
    public int EccCorrections { get; set; }
    public int CrcFailures { get; set; }
    public DateTime StartTime { get; set; }
  public DateTime? EndTime { get; set; }
}

public class MetadataInfo
{
    public string OriginalName { get; set; } = string.Empty;
    public long OriginalLength { get; set; }
    public int TileSize { get; set; }
  public int EccLevel { get; set; }
    public string PayloadType { get; set; } = string.Empty;
}
