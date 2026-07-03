using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxRead.Models;
using FluxRead.Services;
using FluxCore.Framing;
using FluxCore.Imaging;
using FluxCore.Ecc;
using FluxCore.Hashing;
using FluxCore.Compression;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace FluxRead.ViewModels;

/// <summary>
/// Main ViewModel for FluxRead decoder application.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly ScreenCaptureService _screenCaptureService;
    private readonly WindowPositioningService _windowPositioningService;
    private readonly DecoderSessionManager _sessionManager;
 private DecodeSession? _currentSession;

    [ObservableProperty]
    private bool _isDecoding;

    [ObservableProperty]
    private bool _isCapturing;

    [ObservableProperty]
    private double _decodingProgress;

    [ObservableProperty]
  private string _statusMessage = "Ready. Use File ? Start Reading to begin.";

    [ObservableProperty]
    private int _currentFrameIndex;

    [ObservableProperty]
    private int _totalFrames;

    [ObservableProperty]
    private int _eccCorrections;

    [ObservableProperty]
    private int _crcFailures;

    [ObservableProperty]
    private bool _hasSession;

    [ObservableProperty]
    private bool _isCompactMode;

    public ObservableCollection<string> Logs { get; } = new();

  public MainViewModel(
        ILogger<MainViewModel> logger,
        ScreenCaptureService screenCaptureService,
        WindowPositioningService windowPositioningService,
        DecoderSessionManager sessionManager)
    {
     _logger = logger;
   _screenCaptureService = screenCaptureService;
        _windowPositioningService = windowPositioningService;
        _sessionManager = sessionManager;

        // Subscribe to capture events
        _screenCaptureService.ScreenshotCaptured += OnScreenshotCaptured;
        _screenCaptureService.CaptureError += OnCaptureError;
    }

    /// <summary>
    /// Starts decoding from a folder.
    /// </summary>
    public async Task StartFolderDecodeAsync(string folderPath, string outputPath)
    {
   try
        {
            IsDecoding = true;
            HasSession = true;
   Logs.Clear();
  
          _currentSession = new DecodeSession
         {
         InputMode = InputMode.Folder,
         SourceFolder = folderPath,
       OutputPath = outputPath
         };

    AddLog($"Starting decode from folder: {folderPath}");
      StatusMessage = "Scanning for frames...";

      // Step 1: Scan folder for frames
          var frameFiles = Directory.GetFiles(folderPath, "frame_*.png")
        .OrderBy(f => f)
        .ToList();

if (frameFiles.Count == 0)
            {
      throw new Exception("No frame files found in the selected folder.");
  }

       TotalFrames = frameFiles.Count;
 _currentSession.Progress.TotalFrames = TotalFrames;
    AddLog($"Found {TotalFrames} frame files");

     // Step 2: Decode Frame 0 (Metadata)
      StatusMessage = "Loading metadata...";
       DecodingProgress = 0.05;

            var metadataFrameData = await File.ReadAllBytesAsync(frameFiles[0]);
      
       // Use FluxCore FrameDecodingService
         var decodingService = new FrameDecodingService();
       decodingService.DecodeMetadataFrame(metadataFrameData);
            
var metadata = decodingService.Metadata!;
    _currentSession.Metadata = new DecodedMetadata
  {
  Sha256 = metadata.Sha256,
          TileSize = metadata.TileSize,
        EccLevel = metadata.EccPer16,
                PayloadType = metadata.PayloadType.ToString(),
                OriginalName = metadata.OriginalName,
 OriginalLength = metadata.OriginalLength
         };

     AddLog($"Metadata loaded: {metadata.OriginalName} ({metadata.OriginalLength} bytes)");
            AddLog($"Tile Size: {metadata.TileSize}x{metadata.TileSize}, ECC Level: {metadata.EccPer16}");

     // Step 3: Decode all data frames
     StatusMessage = "Decoding frames...";
   var frameImages = new List<byte[]>();

        for (int i = 1; i < frameFiles.Count; i++)
       {
   try
      {
             CurrentFrameIndex = i;
         StatusMessage = $"Decoding frame {i}/{TotalFrames}...";
     DecodingProgress = 0.1 + (0.7 * i / TotalFrames);

       var frameData = await File.ReadAllBytesAsync(frameFiles[i]);
    frameImages.Add(frameData);
        
        _currentSession.Progress.DecodedFrames++;
         }
          catch (Exception ex)
        {
   _currentSession.Progress.FailedFrames++;
         AddLog($"Frame {i} failed to load: {ex.Message}");
         _logger.LogError(ex, "Failed to load frame {Index}", i);
     }
      }

       DecodingProgress = 0.8;

    // Step 4: Decode frames and assemble payload
      StatusMessage = "Assembling payload...";
      AddLog("Decoding and assembling payload...");

            var assembledPayload = decodingService.DecodeFrames(frameImages);

          // Step 5: Verify integrity (already done by DecodeFrames)
            StatusMessage = "Verifying integrity...";
       DecodingProgress = 0.9;

     var computedHash = Sha256Helper.ComputeHash(assembledPayload);
     var isValid = computedHash.SequenceEqual(metadata.Sha256);

            if (isValid)
     {
        AddLog("SHA-256 verification: PASSED ?");
    }
     else
    {
     AddLog("WARNING: SHA-256 mismatch! File may be corrupted.");
                _currentSession.Errors.Add("SHA-256 verification failed");
       }

 // Step 6: Save output
     StatusMessage = "Saving output...";
        DecodingProgress = 0.95;

if (metadata.PayloadType == PayloadType.SevenZip)
      {
    // Save as .7z
    var outputFile = Path.Combine(outputPath, $"{metadata.OriginalName}.7z");
        await File.WriteAllBytesAsync(outputFile, assembledPayload);
     AddLog($"Saved compressed archive: {outputFile}");

// Optional: Extract
          var extract = await Application.Current?.MainPage?.DisplayAlert(
  "Extract Archive?",
                $"File saved as: {outputFile}\n\nWould you like to extract it now?",
  "Extract", "Keep Archive");

    if (extract == true)
          {
           await ExtractArchiveAsync(outputFile, outputPath);
         }
   }
            else
     {
// Save as original file
   var outputFile = Path.Combine(outputPath, metadata.OriginalName);
        await File.WriteAllBytesAsync(outputFile, assembledPayload);
     AddLog($"Saved file: {outputFile}");
            }

            DecodingProgress = 1.0;
    _currentSession.Progress.EndTime = DateTime.UtcNow;

          var duration = _currentSession.Progress.EndTime.Value - _currentSession.Progress.StartTime;
       StatusMessage = $"Decoding complete! ({duration.TotalSeconds:F1}s)";

    // Save final progress
 if (_currentSession.OutputPath != null)
      {
       await _sessionManager.SaveProgressAsync(_currentSession, _currentSession.OutputPath);
      }

     AddLog("=== Decoding Summary ===");
            AddLog($"Total Frames: {TotalFrames}");
            AddLog($"Decoded: {_currentSession.Progress.DecodedFrames}");
        AddLog($"Failed: {_currentSession.Progress.FailedFrames}");
    AddLog($"SHA-256: {(isValid ? "VALID" : "INVALID")}");
     AddLog($"Duration: {duration.TotalSeconds:F1} seconds");

            // Show summary
            await Application.Current?.MainPage?.DisplayAlert(
        "Decoding Complete",
        $"Successfully decoded {_currentSession.Progress.DecodedFrames}/{TotalFrames} frames\n" +
        $"SHA-256: {(isValid ? "VALID ?" : "INVALID ?")}\n" +
    $"Duration: {duration.TotalSeconds:F1}s",
       "OK");
        }
 catch (Exception ex)
        {
      _logger.LogError(ex, "Decoding failed");
            StatusMessage = $"Error: {ex.Message}";
            AddLog($"ERROR: {ex.Message}");

            await Application.Current?.MainPage?.DisplayAlert("Decoding Error", ex.Message, "OK");
        }
        finally
 {
        IsDecoding = false;
        }
    }

    /// <summary>
    /// Starts screen capture mode.
    /// </summary>
    public async Task StartScreenCaptureAsync(double framePeriod, string outputPath)
    {
        try
        {
  IsCapturing = true;
            HasSession = true;
     IsCompactMode = true;
       Logs.Clear();

            _currentSession = new DecodeSession
            {
     InputMode = InputMode.ScreenCapture,
    OutputPath = outputPath
  };

   // Create temp capture folder
  var captureFolder = Path.Combine(Path.GetTempPath(), "FluxRead", $"capture_{Guid.NewGuid():N}");
  _currentSession.SourceFolder = captureFolder;

          AddLog($"Starting screen capture mode (period: {framePeriod}s)");
      StatusMessage = "Initializing screen capture...";

            // Move window to compact mode
await Task.Delay(500); // Brief delay for UI update
            _windowPositioningService.MoveToCompactMode();

            StatusMessage = "Capturing screenshots...";
     AddLog($"Capturing to: {captureFolder}");

            // Start capturing
  _screenCaptureService.StartCapture(framePeriod, captureFolder);
        }
        catch (Exception ex)
        {
         _logger.LogError(ex, "Failed to start screen capture");
            StatusMessage = $"Error: {ex.Message}";
   AddLog($"ERROR: {ex.Message}");

 await Application.Current?.MainPage?.DisplayAlert("Capture Error", ex.Message, "OK");
    IsCapturing = false;
     IsCompactMode = false;
        }
    }

    /// <summary>
    /// Stops screen capture and processes captured frames.
    /// </summary>
    [RelayCommand]
    private async Task StopScreenCapture()
    {
        if (!IsCapturing)
   return;

        try
 {
         AddLog("Stopping screen capture...");
        _screenCaptureService.StopCapture();

            IsCapturing = false;

  // Restore window to normal mode
            _windowPositioningService.RestoreToNormalMode();
          IsCompactMode = false;

     // Process captured frames
            if (_currentSession?.SourceFolder != null && _currentSession.OutputPath != null)
 {
        AddLog($"Processing {_screenCaptureService.CaptureCount} captured frames...");
         await StartFolderDecodeAsync(_currentSession.SourceFolder, _currentSession.OutputPath);
     }
        }
   catch (Exception ex)
        {
     _logger.LogError(ex, "Failed to stop screen capture");
     AddLog($"ERROR: {ex.Message}");
        }
    }

    private async Task ExtractArchiveAsync(string archivePath, string outputFolder)
    {
        try
    {
        var compressionService = new CompressionService();
      var archiveData = await File.ReadAllBytesAsync(archivePath);
     
     await compressionService.DecompressAsync(archiveData, outputFolder);
            
      AddLog($"Extracted archive to: {outputFolder}");
 }
    catch (Exception ex)
     {
  AddLog($"Extraction failed: {ex.Message}");
    _logger.LogError(ex, "Failed to extract archive");
    }
    }

    private void AddLog(string message)
    {
  var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logEntry = $"[{timestamp}] {message}";
       
        Logs.Add(logEntry);
        _logger.LogInformation(message);

        // Keep only last 100 logs
        while (Logs.Count > 100)
      {
            Logs.RemoveAt(0);
  }
    }

    private void OnScreenshotCaptured(object? sender, ScreenshotCapturedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentFrameIndex = e.CaptureIndex;
StatusMessage = $"Captured frame {e.CaptureIndex + 1}...";
        AddLog($"Captured frame {e.CaptureIndex + 1}: {Path.GetFileName(e.FilePath)}");
 });
    }

    private void OnCaptureError(object? sender, CaptureErrorEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
 AddLog($"Capture error: {e.Error}");
 });
    }

    /// <summary>
    /// Exports logs to text file.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSession))]
    private async Task ExportLogs()
    {
  if (_currentSession?.OutputPath == null)
   {
      await Application.Current?.MainPage?.DisplayAlert(
    "Error",
    "No output folder available for log export.",
          "OK");
       return;
  }

    await _sessionManager.ExportLogsAsync(_currentSession, _currentSession.OutputPath);
        
        await Application.Current?.MainPage?.DisplayAlert(
     "Logs Exported",
         $"Logs have been exported to:\n{_currentSession.OutputPath}",
            "OK");
    }

    /// <summary>
    /// Resumes decoding from a saved session.
  /// </summary>
    public async Task ResumeSessionAsync(string progressFilePath)
    {
   try
        {
    AddLog($"Loading session from: {progressFilePath}");
         
      var session = await _sessionManager.LoadProgressAsync(progressFilePath);
     
          if (session == null)
    {
        throw new Exception("Failed to load session progress file.");
  }

          _currentSession = session;

   // Display session info
     var resume = await Application.Current?.MainPage?.DisplayAlert(
    "Resume Decoding Session?",
   $"Found incomplete decoding session:\n\n" +
       $"Source: {session.SourceFolder}\n" +
     $"Progress: {session.Progress.DecodedFrames}/{session.Progress.TotalFrames} frames\n" +
     $"Failed: {session.Progress.FailedFrames} frames\n" +
 $"Started: {session.Progress.StartTime:g}\n\n" +
    "Would you like to resume decoding?",
      "Resume", "Cancel");

       if (resume != true)
      return;

    // Resume decoding
 if (session.SourceFolder != null && session.OutputPath != null)
 {
         AddLog("Resuming decoding session...");
    await StartFolderDecodeAsync(session.SourceFolder, session.OutputPath);
     }
        }
catch (Exception ex)
 {
      _logger.LogError(ex, "Failed to resume session");
    AddLog($"ERROR: {ex.Message}");
       await Application.Current?.MainPage?.DisplayAlert("Resume Error", ex.Message, "OK");
  }
    }
}
