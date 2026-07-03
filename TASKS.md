# Flux Project — Development Tasks

This document tracks implementation progress for the Flux Visual Frame Encoder & Decoder system.

---

## Project Status

**Current Phase:** Foundation & Core Library  
**Last Updated:** 2025-01-XX

---

## 1. FluxCore — Shared Library

### 1.1 Color Mapping
- [x] Define 256-color palette (excluding white)
- [x] Implement `ColorMap` class with byte?color conversion
- [x] Serialize/deserialize color map for metadata frame
- [x] Unit tests for color mapping

### 1.2 Compression
- [x] Implement 7z wrapper (invoke external 7z.exe with max compression)
- [x] Add raw passthrough mode
- [x] Error handling for missing 7z binary
- [x] Hybrid approach with SharpCompress fallback
- [ ] Unit tests for compression/decompression

### 1.3 Hashing
- [x] Implement SHA-256 helper for full payload
- [x] Implement CRC-32 helper for per-frame payload

### 1.4 Error Correction (Reed–Solomon)
- [x] Integrate or implement Reed–Solomon encoder
- [x] Configurable ECC symbols (1–8 per 16 data symbols)
- [x] Implement ECC decoder with correction
- [ ] Unit tests for ECC encode/decode

### 1.5 Frame Layout & Tile Placement
- [x] Define `FrameHeader` struct (EncodingCode, FrameId, TotalFrames, etc.)
- [x] Implement frame header serialization/deserialization
- [x] Calculate tile grid based on window size, tile size, margins, separators
- [x] Place data tiles, ECC tiles, null tiles (white)
- [x] Implement separator logic (white lines every 8×8 tiles)
- [ ] Unit tests for tile layout logic

### 1.6 Metadata Frame (Frame 0)
- [x] Define metadata payload structure (SHA-256, TileSize, ECCPer16, PayloadType, OriginalName, etc.)
- [x] Serialize/deserialize metadata payload
- [x] Embed full color map in metadata frame
- [ ] Unit tests for metadata encoding/decoding

### 1.7 Frame Encoder
- [x] Convert byte array ? tiles with ECC
- [x] Generate frame images (PNG) with proper layout
- [x] Compute per-frame CRC-32
- [ ] Unit tests for frame encoding

### 1.8 Frame Decoder
- [x] Parse frame header from image
- [x] Extract tiles ? byte array (respect margins, separators, null tiles)
- [x] Apply ECC and verify CRC-32
- [ ] Unit tests for frame decoding

---

## 2. FluxCast — Encoder Application

### 2.1 Project Setup
- [x] Create FluxCast .NET 8 MAUI project
- [x] Configure Windows TFM (10.0.26100.0)
- [x] Add project reference to FluxCore
- [x] Install required NuGet packages (SkiaSharp, CommunityToolkit.Mvvm, etc.)

### 2.2 UI Layout
- [x] Main window with file/folder picker (moved to modal)
- [x] Menu bar with File and Help
- [x] New Stream modal/popup dialog
- [x] Tile size selector (2×2, 4×4, 6×6, 8×8)
- [x] ECC level selector (1–8 symbols per 16)
- [x] Compression toggle (raw vs 7z for files)
- [x] Export directly checkbox
- [x] Destination folder selection
- [x] Auto-play checkbox
- [x] Frame period slider
- [x] Frame preview area
- [x] Playback controls (Play/Pause, Next, Previous)
- [x] Progress indicator (encoding progress bar)
- [x] Status messages

### 2.3 Input Handling
- [x] File picker (single file, raw or compressed)
- [x] Folder picker (always compressed)
- [x] Validate input (check 7z availability, file size limits)
- [x] Compress input if needed (handled in encoding pipeline)
- [x] Input validation service with size limits
- [x] Frame count estimation
- [x] File/folder accessibility checks

### 2.4 Encoding Pipeline
- [x] Compute SHA-256 of input payload
- [x] Generate metadata frame (Frame 0)
- [x] Segment payload into frames based on window size
- [x] Generate frames with ECC and CRC-32
- [x] Store frames in memory or disk
- [x] Progress reporting during encoding

### 2.5 Navigation & Playback
- [x] Timed playback (configurable seconds per frame)
- [x] Manual navigation (Next/Previous buttons)
- [x] Play/Pause toggle
- [x] Jump to specific frame number (text input)
- [x] Display current frame in preview area

### 2.6 Save & Resume
- [x] Save all frames to user-selected folder (frame_NNNNNN.png)
- [x] Generate `encode_meta.txt` with encoding settings
- [x] Track first useful frame index, last failed frame index
- [x] Load `encode_meta.txt` to resume encoding session
- [x] Validate frame files before resume
- [x] Handle missing frames gracefully

### 2.7 Optional Features
- [x] Real-time auto-save to temp folder
- [x] JSON progress tracking (progress.json)
- [x] Session management (create/resume/cleanup)
- [x] Resume detection on app start
- [x] Sync guarantee (save before display)
- [x] Export progress/settings to JSON
- [ ] Batch mode UI toggle (save without display)

---

## 3. FluxRead — Decoder Application

### 3.1 Project Setup
- [x] Create FluxRead .NET 8 MAUI project
- [x] Configure Windows TFM (10.0.26100.0)
- [x] Add project reference to FluxCore
- [x] Install required NuGet packages (SkiaSharp, CommunityToolkit.Mvvm, etc.)

### 3.2 UI Layout
- [x] Menu bar with File and Help
- [x] Input source selector modal (Screenshots / Folder)
- [x] Screenshot capture mode:
  - [x] Frame period input (seconds) to sync with encoder
  - [x] Compact window mode (minimize to small progress indicator)
  - [x] Auto-positioning (move away from capture area)
  - [x] Progress display (frame X of Y capturing...)
- [x] Folder decode mode:
  - [x] Folder selection dialog
  - [x] Progress display in main window
  - [x] Frame-by-frame decoding progress
- [x] Frame preview area (status/progress)
- [x] Progress indicator (frames processed, errors corrected)
- [x] Logs panel (detailed + summary)
- [ ] Save/Resume controls

### 3.3 Input Sources
- [x] Live screenshot mode (periodic capture based on frame period)
  - [x] Configure capture interval (sync with encoder's frame period)
  - [x] Minimize window to avoid interference (compact mode)
  - [x] Auto-position window outside capture area
  - [x] Save captured frames to temp folder
- [x] Folder mode (read encoder output)
  - [x] Select folder containing frame_NNNNNN.png files
  - [x] Load and decode all frames in folder
  - [x] Progress tracking during decode

### 3.4 Frame Ingestion
- [x] Scan folder for frame images
- [x] Sort frames by Frame ID
- [x] Detect missing frames
- [x] Parse frame headers from images (via FluxCore)
- [x] Extract metadata from Frame 0
- [x] Screenshot capture and save

### 3.5 Decoding Pipeline
- [x] Decode tiles ? byte array (via FluxCore FrameDecodingService)
- [x] Apply Reed–Solomon ECC
- [x] Verify per-frame CRC-32
- [x] Assemble payload segments
- [x] Compute final SHA-256 and compare with metadata

### 3.6 Output Handling
- [x] Save reconstructed file or 7z archive
- [x] Optional: extract 7z archive automatically
- [x] Display integrity result (SHA-256 match/mismatch)

### 3.7 Logging
- [x] Detailed logs (frame processing, errors)
- [x] Summary report (frames processed, SHA-256 result, duration)
- [x] Export logs to TXT/JSON

### 3.8 Resume & Progress
- [x] Load progress.json from encoder
- [x] Save decoder_progress.json with last processed frame
- [x] Retry failed frames (via resume)
- [x] Allow user to replace corrupted images and resume
- [x] Auto-detect incomplete sessions on startup
- [x] Manual resume from File menu
- [x] Export logs to text file

---

## 4. Testing & Quality

### 4.1 Unit Tests (FluxCore)
- [ ] Color mapping tests
- [ ] Compression/decompression tests
- [ ] Hashing tests (SHA-256, CRC-32)
- [ ] ECC encode/decode tests
- [ ] Frame header serialization tests
- [ ] Metadata frame tests
- [ ] Tile layout tests

### 4.2 Integration Tests
- [ ] End-to-end encode/decode test (small file)
- [ ] End-to-end encode/decode test (folder with multiple files)
- [ ] Test with different tile sizes (2×2, 4×4, 6×6, 8×8)
- [ ] Test with different ECC levels (1–8)
- [ ] Test with missing frames (ECC recovery)
- [ ] Test with corrupted frames (CRC failure, ECC correction)

### 4.3 UI/UX Testing
- [ ] Test FluxCast on different window sizes (min 800×600)
- [ ] Test FluxRead screenshot capture mode
- [ ] Test save/resume workflows
- [ ] Test navigation controls (timed playback, manual, jump)

---

## 5. Documentation

- [x] README.md (overview, build instructions, specs)
- [ ] API documentation (XML comments in FluxCore)
- [ ] User guide for FluxCast (encoder)
- [ ] User guide for FluxRead (decoder)
- [ ] Troubleshooting guide (7z not found, ECC failures, etc.)
- [ ] Contributing guidelines

---

## 6. Nice-to-Have / Future Enhancements

- [ ] Batch decode multiple frame sets sequentially
- [ ] Health overlay (color-coded tiles: data/ECC/empty/corrected)
- [ ] Parallel decoding for performance
- [ ] Memory-friendly streaming for large datasets
- [ ] Automated 7z extraction after successful verification
- [ ] Export/import session as portable bundle
- [ ] Multi-platform support (Android, iOS, macOS)
- [ ] CI/CD pipeline (GitHub Actions)

---

## Change Log

| Date       | Task | Status    |
|------------|-------------------------------------------|-----------|
| 2025-01-XX | Created TASKS.md | Done   |
| 2025-01-XX | Fixed Windows TFM version conflicts | Done |
| 2025-01-XX | Created README.md          | Done |
| 2025-01-XX | Created CODING_GUIDELINES.md | Done |
| 2025-01-XX | Implement FluxCore color mapping   | Done      |
| 2025-01-XX | Implement FluxCore hashing (SHA-256, CRC-32) | Done      |
| 2025-01-XX | Implement FluxCore compression (7z wrapper) | Done      |
| 2025-01-XX | Implement FluxCore frame layout & headers | Done    |
| 2025-01-XX | Implement FluxCore Reed–Solomon ECC | Done |
| 2025-01-XX | Implement FluxCore metadata frame | Done |
| 2025-01-XX | Implement FluxCore frame encoder/decoder | Done |
| TBD        | Unit tests for all FluxCore components | In Progress |
| TBD        | Implement FluxCast UI     | Pending   |
| TBD   | Implement FluxRead UI  | Pending|

