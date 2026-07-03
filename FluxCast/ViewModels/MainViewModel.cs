using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxCore.Compression;
using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;
using FluxCore.Imaging;
using FluxCast.Views;
using FluxCast.Services;
using FluxCast.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace FluxCast.ViewModels;

/// <summary>
/// Main ViewModel for the FluxCast encoder application.
/// </summary>
public partial class MainViewModel : ObservableObject, IQueryAttributable
{
 private readonly ILogger<MainViewModel> _logger;
private readonly CompressionService _compressionService;
    private readonly SessionManager _sessionManager;
    private StreamConfiguration? _currentConfiguration;
    private SessionData? _currentSession;
    private System.Timers.Timer? _playbackTimer;

    [ObservableProperty]
    private int _currentFrameIndex;

    [ObservableProperty]
 private int _totalFrames;

    [ObservableProperty]
    private bool _isEncoding;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _encodingProgress;

    [ObservableProperty]
  private string _statusMessage = "No stream loaded. Use File ? New Stream to begin.";

    [ObservableProperty]
private byte[]? _currentFrameImage;

    [ObservableProperty]
    private bool _hasStream;

    public ObservableCollection<byte[]> EncodedFrames { get; } = new();

    public MainViewModel(ILogger<MainViewModel> logger, SessionManager sessionManager)
 {
        _logger = logger;
        _sessionManager = sessionManager;
        _compressionService = new CompressionService(logger: logger as ILogger<CompressionService>);
 }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
if (query.TryGetValue("Configuration", out var configObj) && configObj is StreamConfiguration config)
 {
       _currentConfiguration = config;
    _ = StartEncodingAsync(config);
       }
     else if (query.TryGetValue("ResumeMetadata", out var metadataObj) && metadataObj is EncodeMetadata metadata)
{
  _ = ResumeFromMetadataAsync(metadata);
 }
  else if (query.TryGetValue("ResumeSession", out var sessionObj) && sessionObj is SessionData session)
   {
       _ = ResumeFromSessionAsync(session);
 }
    }

    private async Task StartEncodingAsync(StreamConfiguration config)
    {
        try
   {
  IsEncoding = true;
  HasStream = false;
     EncodedFrames.Clear();
StatusMessage = "Initializing encoding...";

     // Create session
            _currentSession = await _sessionManager.CreateSessionAsync(config);
            _currentConfiguration = config;

       // Step 1: Compress if needed
  byte[] payload;
     if (config.EnableCompression || config.IsFolder)
      {
   StatusMessage = "Compressing...";
EncodingProgress = 10;
 var compressionResult = await _compressionService.CompressAsync(config.SelectedPath);
  payload = compressionResult.Data;
 _logger.LogInformation("Compressed {Original} ? {Compressed} bytes",
     compressionResult.OriginalSize, compressionResult.CompressedSize);
     }
      else
{
    payload = await File.ReadAllBytesAsync(config.SelectedPath);
  }

   EncodingProgress = 30;

     // Step 2: Compute SHA-256
  StatusMessage = "Computing hash...";
  var sha256 = Sha256Helper.ComputeHash(payload);
      EncodingProgress = 40;

   // Step 3: Create metadata
            var metadata = new MetadataPayload(
        sha256,
       (byte)config.TileSize,
  (byte)config.EccLevel,
      separatorEvery: 8,
    algorithm: 1,
                config.EnableCompression || config.IsFolder ? PayloadType.SevenZip : PayloadType.Raw,
Path.GetFileName(config.SelectedPath) ?? "unknown",
   payload.Length,
 ColorMap.Default);

      // Step 4: Encode frames
        StatusMessage = "Encoding frames...";
   EncodingProgress = 50;
      var layout = new FrameLayout(1920, 1080, config.TileSize);
var ecc = new ReedSolomonEcc(config.EccLevel);
     var encodingService = new FrameEncodingService(layout, ColorMap.Default, ecc);

     var frames = encodingService.EncodePayload(payload, metadata);
 
         TotalFrames = frames.Count;
 _currentSession.Progress.TotalFrames = TotalFrames;

 // Step 5: Save frames with real-time progress
       StatusMessage = "Saving frames...";
            
       for (int i = 0; i < frames.Count; i++)
       {
         // CRITICAL: Save to disk BEFORE adding to memory
    await _sessionManager.SaveFrameAsync(_currentSession, frames[i], i);

  // Update UI progress
   EncodingProgress = 50 + (30.0 * (i + 1) / frames.Count);
         StatusMessage = $"Saving frames... {i + 1}/{frames.Count}";

    // Add to memory for preview (if not exporting directly)
  if (!config.ExportDirectly)
       {
     EncodedFrames.Add(frames[i]);
}
            }

       EncodingProgress = 80;

            // Step 6: Finalize based on mode
       if (config.ExportDirectly)
{
   // Convert temp to permanent
     await _sessionManager.ConvertToPermanentAsync(_currentSession, config.DestinationFolder);
        await _sessionManager.UpdateStatusAsync(_currentSession, "completed");
   
  // Cleanup temp
       await _sessionManager.CleanupSessionAsync(_currentSession.SessionId);
          
     StatusMessage = $"Encoded and saved {TotalFrames} frames to {config.DestinationFolder}";
    }
     else
   {
// Preview mode - frames in memory + temp folder
     await _sessionManager.UpdateStatusAsync(_currentSession, "completed");
                
CurrentFrameIndex = 0;
   if (EncodedFrames.Count > 0)
{
    CurrentFrameImage = EncodedFrames[0];
  }

           EncodingProgress = 100;
 StatusMessage = $"Encoded {TotalFrames} frames. Ready for preview (backup in temp).";
       HasStream = true;

    // Start auto-play if configured
 if (config.AutoPlay)
      {
  StartPlayback(config.FramePeriod);
    }
  }

        _logger.LogInformation("Encoding complete: {Frames} frames", TotalFrames);
  }
        catch (Exception ex)
   {
   _logger.LogError(ex, "Encoding failed");
       if (_currentSession != null)
            {
    await _sessionManager.UpdateStatusAsync(_currentSession, "failed");
        }
       StatusMessage = $"Error: {ex.Message}";
     await Shell.Current.DisplayAlert("Encoding Error", ex.Message, "OK");
 }
  finally
  {
        IsEncoding = false;
   }
    }

    private async Task SaveFramesToDiskAsync(List<byte[]> frames, string outputDir)
    {
 Directory.CreateDirectory(outputDir);

        int lastFailedFrame = -1;

   for (int i = 0; i < frames.Count; i++)
        {
    try
{
     var framePath = Path.Combine(outputDir, $"frame_{i:D6}.png");
      await File.WriteAllBytesAsync(framePath, frames[i]);

  // Update progress
  EncodingProgress = 80 + (20.0 * i / frames.Count);
     }
      catch (Exception ex)
   {
       _logger.LogError(ex, "Failed to save frame {Index}", i);
   lastFailedFrame = i;
    }
        }

 // Save metadata
   var metaPath = Path.Combine(outputDir, "encode_meta.txt");
   var metaContent = GenerateMetadata(lastFailedFrame);
await File.WriteAllTextAsync(metaPath, metaContent);

     if (lastFailedFrame >= 0)
  {
 StatusMessage = $"Encoded {TotalFrames} frames with {lastFailedFrame + 1} failure(s). Check {outputDir}";
   }
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
 private void NextFrame()
    {
   if (CurrentFrameIndex < TotalFrames - 1)
      {
     CurrentFrameIndex++;
   CurrentFrameImage = EncodedFrames[CurrentFrameIndex];
        }
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void PreviousFrame()
  {
        if (CurrentFrameIndex > 0)
        {
    CurrentFrameIndex--;
   CurrentFrameImage = EncodedFrames[CurrentFrameIndex];
        }
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void TogglePlayback()
    {
        if (IsPlaying)
        {
 StopPlayback();
  }
        else
{
  StartPlayback(_currentConfiguration?.FramePeriod ?? 1.0);
  }
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private async Task JumpToFrameAsync(string? frameNumberText)
 {
        if (string.IsNullOrWhiteSpace(frameNumberText))
      {
     await Shell.Current.DisplayAlert("Invalid Input", "Please enter a frame number.", "OK");
   return;
        }

        if (!int.TryParse(frameNumberText, out int frameNumber))
{
  await Shell.Current.DisplayAlert("Invalid Input", "Frame number must be a valid integer.", "OK");
        return;
}

        if (frameNumber < 0 || frameNumber >= TotalFrames)
 {
         await Shell.Current.DisplayAlert(
     "Invalid Frame", 
      $"Frame number must be between 0 and {TotalFrames - 1}.", 
         "OK");
       return;
        }

        // Jump to the frame
     CurrentFrameIndex = frameNumber;
        CurrentFrameImage = EncodedFrames[frameNumber];

    _logger.LogInformation("Jumped to frame {Frame}", frameNumber);
    }

    private void StartPlayback(double framePeriod)
    {
     if (_playbackTimer != null)
     {
      StopPlayback();
}

        IsPlaying = true;
   _playbackTimer = new System.Timers.Timer(framePeriod * 1000);
        _playbackTimer.Elapsed += (s, e) =>
        {
      MainThread.BeginInvokeOnMainThread(() =>
  {
         if (CurrentFrameIndex < TotalFrames - 1)
      {
        NextFrame();
       }
  else
         {
           StopPlayback();
           }
     });
        };
  _playbackTimer.Start();
 }

    private void StopPlayback()
    {
 IsPlaying = false;
     _playbackTimer?.Stop();
 _playbackTimer?.Dispose();
  _playbackTimer = null;
    }

    private bool CanNavigate() => HasStream && !IsPlaying && !IsEncoding;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportFramesAsync()
 {
   // This will be used when frames are in memory and user wants to save them later
 // Implementation similar to SaveFramesToDiskAsync
        await Task.CompletedTask;
    }

    private bool CanExport() => HasStream && !IsEncoding;

    private string GenerateMetadata(int lastFailedFrameIndex = -1)
 {
if (_currentConfiguration == null)
    return string.Empty;

return $@"Version=1
EncodingCode=1
TileSize={(int)_currentConfiguration.TileSize}
ECCPer16={_currentConfiguration.EccLevel}
SeparatorEvery=8
FrameWidthPx=1920
FrameHeightPx=1080
TotalFrames={TotalFrames}
FirstUsefulFrameIndex=1
LastFailedFrameIndex={lastFailedFrameIndex}
PayloadType={(_currentConfiguration.EnableCompression || _currentConfiguration.IsFolder ? "7z" : "raw")}
OriginalName={Path.GetFileName(_currentConfiguration.SelectedPath)}
ColorMap=EmbeddedInFrame0";
    }

    /// <summary>
    /// Resumes an encoding session from metadata file.
    /// </summary>
    private async Task ResumeFromMetadataAsync(EncodeMetadata metadata)
    {
        try
        {
     IsEncoding = true;
   StatusMessage = "Loading frames from disk...";
 EncodedFrames.Clear();

  // Load frames from disk
    var metadataService = new MetadataFileService(_logger as ILogger<MetadataFileService>);
  
  var progress = new Progress<int>(loaded =>
{
       EncodingProgress = (double)loaded / metadata.TotalFrames;
       StatusMessage = $"Loading frames... {loaded}/{metadata.TotalFrames}";
  });

   var frames = await metadataService.LoadFramesAsync(metadata, progress);

// Add to collection
            foreach (var frame in frames)
            {
    if (frame.Length > 0) // Skip empty placeholders
    {
     EncodedFrames.Add(frame);
       }
      }

      TotalFrames = EncodedFrames.Count;
   CurrentFrameIndex = 0;
   
            if (EncodedFrames.Count > 0)
   {
  CurrentFrameImage = EncodedFrames[0];
           HasStream = true;
   StatusMessage = $"Resumed session: {TotalFrames} frames loaded from {metadata.FramesDirectory}";
      }
      else
      {
       StatusMessage = "No valid frames found in the session.";
    }

    _logger.LogInformation("Resumed session: {Frames} frames loaded", TotalFrames);
     }
        catch (Exception ex)
  {
        _logger.LogError(ex, "Failed to resume session");
    StatusMessage = $"Resume error: {ex.Message}";
            await Shell.Current.DisplayAlert("Resume Error", ex.Message, "OK");
   }
    finally
  {
            IsEncoding = false;
     EncodingProgress = 0;
        }
    }

    /// <summary>
 /// Resumes an encoding session from SessionData (JSON).
 /// </summary>
 private async Task ResumeFromSessionAsync(SessionData session)
    {
  try
      {
     IsEncoding = true;
   StatusMessage = "Resuming session...";
 EncodedFrames.Clear();

  _currentSession = session;

  // Load frames from temp folder
    var progress = new Progress<int>(loaded =>
{
      EncodingProgress = (double)loaded / session.Progress.TotalFrames;
   StatusMessage = $"Loading frames... {loaded}/{session.Progress.TotalFrames}";
  });

   var frames = new List<byte[]>();
       for (int i = 0; i < session.Progress.TotalFrames; i++)
     {
      var framePath = Path.Combine(session.Frames.TempFolder, $"frame_{i:D6}.png");
     
     if (File.Exists(framePath))
   {
      var frameData = await File.ReadAllBytesAsync(framePath);
    frames.Add(frameData);
  EncodedFrames.Add(frameData);
    }
   else
         {
        _logger?.LogWarning("Frame {Index} not found during resume", i);
 }

        ((IProgress<int>)progress).Report(i + 1);
   }

    TotalFrames = EncodedFrames.Count;
 CurrentFrameIndex = 0;
   
     if (EncodedFrames.Count > 0)
   {
  CurrentFrameImage = EncodedFrames[0];
           HasStream = true;
 StatusMessage = $"Resumed session: {TotalFrames} frames loaded";
      }
      else
{
  StatusMessage = "No valid frames found in session.";
    }

    _logger.LogInformation("Resumed session: {Frames} frames loaded", TotalFrames);
     }
        catch (Exception ex)
{
   _logger.LogError(ex, "Failed to resume session");
    StatusMessage = $"Resume error: {ex.Message}";
   await Shell.Current.DisplayAlert("Resume Error", ex.Message, "OK");
   }
finally
  {
          IsEncoding = false;
     EncodingProgress = 0;
     }
    }
}
