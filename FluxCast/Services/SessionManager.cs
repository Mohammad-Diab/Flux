using FluxCast.Models;
using FluxCast.Views;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FluxCast.Services;

/// <summary>
/// Manages encoding sessions with real-time progress tracking and auto-save.
/// </summary>
public class SessionManager
{
    private readonly ILogger<SessionManager>? _logger;
    private readonly string _baseTempFolder;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SessionManager(ILogger<SessionManager>? logger = null)
    {
        _logger = logger;
        _baseTempFolder = Path.Combine(Path.GetTempPath(), "FluxCast");
      Directory.CreateDirectory(_baseTempFolder);
    }

    /// <summary>
    /// Creates a new encoding session.
    /// </summary>
    public async Task<SessionData> CreateSessionAsync(StreamConfiguration config)
    {
    var session = new SessionData
{
            SessionId = Guid.NewGuid().ToString(),
         Status = "encoding",
            EncodingConfig = new EncodingConfig
            {
        SourcePath = config.SelectedPath,
      IsFolder = config.IsFolder,
    TileSize = (int)config.TileSize,
 EccLevel = config.EccLevel,
                EnableCompression = config.EnableCompression,
         FrameWidthPx = 1920,
         FrameHeightPx = 1080
       },
      Progress = new ProgressInfo
   {
 StartTime = DateTime.UtcNow,
    LastUpdateTime = DateTime.UtcNow
            }
        };

        // Create session folder
        var sessionFolder = Path.Combine(_baseTempFolder, $"session_{session.SessionId}");
        Directory.CreateDirectory(sessionFolder);

        session.Frames.TempFolder = sessionFolder;
        session.Frames.StoredInMemory = !config.ExportDirectly;

      // Save initial session
        await SaveSessionAsync(session);

        _logger?.LogInformation("Created session {SessionId} in {Folder}", session.SessionId, sessionFolder);

        return session;
    }

    /// <summary>
    /// Saves a frame to the session folder and updates progress.
    /// </summary>
    public async Task SaveFrameAsync(SessionData session, byte[] frameData, int frameIndex)
    {
      var framePath = Path.Combine(session.Frames.TempFolder, $"frame_{frameIndex:D6}.png");
        
        try
        {
        await File.WriteAllBytesAsync(framePath, frameData);

    // Add to frame list
            session.Frames.FrameFiles.Add(new FrameFileInfo
     {
      Index = frameIndex,
  File = $"frame_{frameIndex:D6}.png",
      Size = frameData.Length
            });

// Update progress
            session.Progress.EncodedFrames = frameIndex + 1;
            session.Progress.CurrentFrameIndex = frameIndex;
  session.Progress.LastUpdateTime = DateTime.UtcNow;

       // Save progress immediately
     await SaveSessionAsync(session);

            _logger?.LogDebug("Saved frame {Index} ({Size} bytes)", frameIndex, frameData.Length);
        }
        catch (Exception ex)
        {
        var error = $"Failed to save frame {frameIndex}: {ex.Message}";
          session.Errors.Add(error);
    _logger?.LogError(ex, error);
     }
    }

    /// <summary>
    /// Saves the session progress to JSON file.
    /// </summary>
    public async Task SaveSessionAsync(SessionData session)
    {
        var progressPath = Path.Combine(session.Frames.TempFolder, "progress.json");
        
 try
        {
          var json = JsonSerializer.Serialize(session, JsonOptions);
            await File.WriteAllTextAsync(progressPath, json);
  }
   catch (Exception ex)
   {
            _logger?.LogError(ex, "Failed to save session progress");
     }
    }

    /// <summary>
    /// Updates session status.
    /// </summary>
    public async Task UpdateStatusAsync(SessionData session, string status)
    {
        session.Status = status;
  session.Progress.LastUpdateTime = DateTime.UtcNow;
        await SaveSessionAsync(session);

        _logger?.LogInformation("Session {SessionId} status: {Status}", session.SessionId, status);
    }

    /// <summary>
    /// Loads a session from progress.json file.
    /// </summary>
    public async Task<SessionData?> LoadSessionAsync(string sessionFolder)
    {
    var progressPath = Path.Combine(sessionFolder, "progress.json");
        
      if (!File.Exists(progressPath))
    {
       _logger?.LogWarning("Progress file not found: {Path}", progressPath);
       return null;
        }

        try
        {
          var json = await File.ReadAllTextAsync(progressPath);
  var session = JsonSerializer.Deserialize<SessionData>(json, JsonOptions);
            
  _logger?.LogInformation("Loaded session {SessionId}", session?.SessionId);
       
            return session;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load session from {Path}", progressPath);
            return null;
    }
    }

    /// <summary>
    /// Gets all incomplete sessions.
    /// </summary>
    public List<SessionData> GetIncompleteSessions()
    {
        var sessions = new List<SessionData>();

if (!Directory.Exists(_baseTempFolder))
     return sessions;

   var sessionDirs = Directory.GetDirectories(_baseTempFolder, "session_*");

        foreach (var dir in sessionDirs)
        {
            var session = LoadSessionAsync(dir).Result;
            
     if (session != null && session.Status != "completed")
    {
           sessions.Add(session);
  }
        }

        _logger?.LogInformation("Found {Count} incomplete sessions", sessions.Count);

        return sessions;
    }

    /// <summary>
    /// Cleans up a session folder.
    /// </summary>
    public async Task CleanupSessionAsync(string sessionId)
    {
        var sessionFolder = Path.Combine(_baseTempFolder, $"session_{sessionId}");
        
    if (Directory.Exists(sessionFolder))
        {
  try
  {
       Directory.Delete(sessionFolder, true);
    _logger?.LogInformation("Cleaned up session {SessionId}", sessionId);
          }
         catch (Exception ex)
      {
          _logger?.LogError(ex, "Failed to cleanup session {SessionId}", sessionId);
}
     }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Converts temp session to permanent folder.
    /// </summary>
public async Task ConvertToPermanentAsync(SessionData session, string destinationFolder)
    {
  Directory.CreateDirectory(destinationFolder);

        // Copy all frames
        foreach (var frameFile in session.Frames.FrameFiles)
        {
          var sourcePath = Path.Combine(session.Frames.TempFolder, frameFile.File);
   var destPath = Path.Combine(destinationFolder, frameFile.File);
     
         if (File.Exists(sourcePath))
      {
    File.Copy(sourcePath, destPath, true);
}
        }

     // Copy progress.json as session_meta.json
        var progressPath = Path.Combine(session.Frames.TempFolder, "progress.json");
        var destMetaPath = Path.Combine(destinationFolder, "session_meta.json");
        
        if (File.Exists(progressPath))
    {
   File.Copy(progressPath, destMetaPath, true);
 }

        _logger?.LogInformation("Converted session {SessionId} to {Folder}", session.SessionId, destinationFolder);

    await Task.CompletedTask;
    }
}
