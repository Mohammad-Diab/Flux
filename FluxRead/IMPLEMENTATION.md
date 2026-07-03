# FluxRead - Decoder Application Implementation Plan

## Overview
FluxRead is the decoder companion to FluxCast, designed to reconstruct files from visual frames either captured from screen or loaded from disk.

---

## Architecture

### Input Modes

#### 1. **From Screen (Live Capture)**
- User enters frame period (e.g., 2.0 seconds)
- Window minimizes to compact mode (300x100px)
- Auto-positions to corner (avoids capture area)
- Captures screenshots at regular intervals
- Saves to temp folder: `capture_NNNNNN.png`
- Processes frames in real-time or batch

#### 2. **From Folder (Batch Decode)**
- User selects folder with `frame_NNNNNN.png` files
- Optionally loads `progress.json` or `encode_meta.txt`
- Decodes all frames sequentially
- Shows progress in main window

---

## UI Components

### MainPage (Normal Mode)
```
??????????????????????????????????????????????????
? FluxRead - Decoder        [_][?][X]   ?
??????????????????????????????????????????????????
? Menu: [File] [Help]   ?
??????????????????????????????????????????????????
?           ?
?  ???????????????????????????????????????????? ?
?  ?         [No Decoding Active]          ? ?
?  ?   ? ?
?  ?   Use File ? Start Reading to begin     ? ?
?  ???????????????????????????????????????????? ?
?       ?
?  Status: Ready    ?
??????????????????????????????????????????????????
```

### Compact Mode (During Screen Capture)
```
???????????????????????????????????
? FluxRead - Capturing    [_][X]  ?
? Frame 42 of ??? ...       ?
? ?????????????????????  60%?
? Press ESC to stop      ?
???????????????????????????????????
(300x100px, top-right corner)
```

### Input Mode Selection Dialog
```
??????????????????????????????????????????
? Start Reading          ?
??????????????????????????????????????????
? ?
?  Select Input Source:         ?
?         ?
?  ? From Screen (Live Capture) ?
?    Frame Period: [2.0] seconds         ?
?    [Start Capturing]   ?
?        ?
?  ? From Folder         ?
?    Folder: [Browse...]       ?
?    [Start Decoding]    ?
?   ?
?  [Cancel]        ?
??????????????????????????????????????????
```

---

## Implementation Tasks

### Phase 1: Project Setup ?
- [x] Add FluxCore reference
- [x] Add NuGet packages (MVVM, SkiaSharp)
- [ ] Create folder structure

### Phase 2: Core Services
- [ ] `ScreenCaptureService` - Screenshot capture
- [ ] `FrameDecodingService` - Decode frames to data
- [ ] `DecoderSessionManager` - Track decode progress
- [ ] `WindowPositioningService` - Auto-position window

### Phase 3: ViewModels
- [ ] `MainViewModel` - Main decode logic
- [ ] `InputModeViewModel` - Input selection dialog
- [ ] `CaptureViewModel` - Compact capture mode

### Phase 4: UI
- [ ] MainPage.xaml - Main decode interface
- [ ] InputModePage.xaml - Input selection dialog
- [ ] CompactCapturePage.xaml - Minimal capture UI
- [ ] AppShell with menu

### Phase 5: Decoding Pipeline
- [ ] Frame ingestion (scan/capture)
- [ ] Metadata extraction (Frame 0)
- [ ] Tile decoding with ECC
- [ ] CRC-32 verification
- [ ] Payload assembly
- [ ] SHA-256 verification
- [ ] File output

### Phase 6: Features
- [ ] Resume capability
- [ ] Logging system
- [ ] Progress tracking
- [ ] Error handling

---

## File Structure

```
FluxRead/
??? ViewModels/
?   ??? MainViewModel.cs
?   ??? InputModeViewModel.cs
?   ??? CaptureViewModel.cs
??? Views/
?   ??? MainPage.xaml
?   ??? InputModePage.xaml
?   ??? CompactCapturePage.xaml
??? Services/
?   ??? ScreenCaptureService.cs
?   ??? FrameDecodingService.cs
?   ??? DecoderSessionManager.cs
?   ??? WindowPositioningService.cs
??? Models/
? ??? DecodeSession.cs
?   ??? CapturedFrame.cs
?   ??? DecodeResult.cs
??? App.xaml
??? AppShell.xaml
??? MauiProgram.cs
```

---

## Key Classes

### ScreenCaptureService
```csharp
public class ScreenCaptureService
{
    public async Task<byte[]> CaptureScreenAsync(Rectangle area);
    public async Task StartPeriodicCaptureAsync(double intervalSeconds, Action<byte[]> onCapture);
  public void StopCapture();
}
```

### FrameDecodingService
```csharp
public class FrameDecodingService
{
    public Task<FrameData> DecodeFrameAsync(byte[] imageData);
    public Task<MetadataPayload> ExtractMetadataAsync(byte[] frame0);
    public Task<byte[]> AssemblePayloadAsync(List<FrameData> frames);
    public bool VerifyIntegrity(byte[] payload, byte[] expectedSha256);
}
```

### DecoderSessionManager
```csharp
public class DecoderSessionManager
{
    public Task<DecodeSession> CreateSessionAsync(InputMode mode);
    public Task SaveProgressAsync(DecodeSession session);
    public Task<DecodeSession?> LoadSessionAsync(string path);
    public Task ExportLogsAsync(DecodeSession session, string path);
}
```

---

## Workflow Examples

### Workflow 1: Screen Capture
```
1. User: File ? Start Reading
2. Dialog: Select "From Screen"
3. User: Enter frame period: 2.0 seconds
4. User: Click "Start Capturing"
5. Window: Minimize to compact mode, move to corner
6. System: Capture screen every 2 seconds
7. System: Save to temp/capture_000001.png, capture_000002.png...
8. System: Process frames in background
9. User: Press ESC or "Stop" when done
10. System: Decode all captured frames
11. System: Reconstruct file, verify SHA-256
12. System: Save output, show report
```

### Workflow 2: Folder Decode
```
1. User: File ? Start Reading
2. Dialog: Select "From Folder"
3. User: Browse to folder with frames
4. User: Click "Start Decoding"
5. System: Scan folder for frame_*.png
6. System: Load progress.json (if exists)
7. System: Decode frame 0 (metadata)
8. System: Decode frames 1...N with ECC
9. System: Assemble payload
10. System: Verify SHA-256
11. System: Save reconstructed file
12. System: Show detailed report
```

---

## Next Steps

1. **Create folder structure** in FluxRead
2. **Implement ScreenCaptureService** (Windows screenshot API)
3. **Create InputModePage** dialog
4. **Implement MainViewModel** with decode logic
5. **Test screen capture** functionality
6. **Implement folder mode**
7. **Add logging and progress tracking**

---

## Technical Notes

### Screen Capture (Windows)
- Use `Graphics.CopyFromScreen()` or Windows API
- Capture at specified intervals
- Save as PNG to temp folder
- Process asynchronously

### Window Positioning
- Use Windows API to set window position/size
- Calculate safe position (top-right corner)
- Avoid taskbar and system tray areas

### Compact Mode
- Set window size to 300x100
- Remove decorations (minimal chrome)
- Always on top (optional)
- Show only essential progress info

---

This is a comprehensive plan for FluxRead implementation!
