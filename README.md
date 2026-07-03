# Flux — Visual Frame Encoder & Decoder (FluxCast / FluxRead)

Two complementary .NET8 MAUI desktop apps that convert files/folders to a sequence of visual frames (tiles) and reconstruct them back with error correction and integrity verification.

- Encoder (`FluxCast`): Produces visual frames (images) from input data using a byte→color mapping, Reed–Solomon ECC, and structured headers.
- Decoder (`FluxRead`): Rebuilds original data from frames, validates per-frame CRC, applies ECC, and verifies full SHA-256.
- Core (`FluxCore`): Shared library for compression, hashing, ECC, and frame layout logic.

This repository targets Windows by default on Windows. MacCatalyst builds require macOS.

---

## Project Structure

- `FluxCore/` — Shared library: compression (7z), hashing, Reed–Solomon, frame layout, color map.
- `FluxCast/` — Encoder MAUI app: file/folder ingestion, frame generation, navigation, save/resume.
- `FluxRead/` — Decoder MAUI app: frame ingestion (folder/screenshots), ECC/CRC/SHA verification, resume.

Status: initial scaffolding; core features will be implemented iteratively.

---

## Prerequisites

- .NET8 SDK
- Visual Studio2022 (17.10+) with the “.NET Multi-platform App UI development” workload
- Windows11 SDK26100 (Windows TFM:10.0.26100.0)
-7-Zip (7z) installed and available in PATH (used for maximum compression)
- Optional (Mac builds):
 - macOS with Xcode (for MacCatalyst)
 - `dotnet workload install maui`

CLI setup:
- `dotnet workload restore`
- On macOS (optional): `dotnet workload install maui`

Notes:
- On Windows, only Windows TFMs build/run. MacCatalyst builds require macOS.

---

## Build & Run

Visual Studio:
1. Open the solution.
2. Right-click `FluxCast` or `FluxRead` → Set as Startup Project.
3. Build Solution (Ctrl+Shift+B), then Start Debugging (F5).

CLI:
- Restore: `dotnet restore`
- Build encoder: `dotnet build ./FluxCast/FluxCast.csproj -c Release -f net8.0-windows10.0.26100.0`
- Build decoder: `dotnet build ./FluxRead/FluxRead.csproj -c Release -f net8.0-windows10.0.26100.0`
- Run (debug): `dotnet run --project ./FluxCast/FluxCast.csproj -f net8.0-windows10.0.26100.0`

---

## Core Concepts

- Tile sizes:2×2,4×4,6×6,8×8 px (each tile encodes1 byte).
- Margins:¼ tile width (≥1 px).
- Separators: White line½ tile width (≥2 px) every8×8 tiles.
- Null tiles: White tiles mean “no data” (reserved). The256-color map excludes white.
- Screen/frame resolution: Responsive to window size (min800×600 px).
- ECC: Reed–Solomon; user selects ECC symbols per16 symbols (1–8). ECC tiles are placed per layout scheme.
- Frames:
 - Header per frame: Frame ID, total frames, CRC-32, frame dimensions, encoding code (1 byte, current =1)
 - First (metadata) frame uses8×8 tiles and includes:
 - SHA-256 of entire payload (post-compression if applicable)
 - Encoding parameters (tile size, ECC count, frame dimensions, algorithm, version)
 - File segmentation details
 - Full color→byte map (0–255 unique colors excluding white)

---

## Encoder (FluxCast)

User Workflow:
1. **Menu**: File → New Stream
2. **Configuration Dialog**: User configures:
   - Input (File or Folder)
   - Tile Size (2×2, 4×4, 6×6, 8×8 px)
   - ECC Level (1-8 symbols per 16)
   - Compression (checkbox, auto-enabled for folders)
   - Export Directly (saves frames immediately vs preview mode)
   - Destination Folder (for export or temporary storage)
   - Auto-Play (automatic frame advancement)
   - Frame Period (seconds between frames, active when auto-play enabled)
3. **Encoding**: 
   - If "Export Directly" is checked: Shows progress bar, saves frames to destination
   - If unchecked: Encodes to memory, displays in main window for preview
4. **Main Window**:
   - Empty state when no stream loaded
   - Frame preview with navigation controls (Previous/Next/Play/Pause)
   - Export button to save frames later
5. **Resume**: File → Resume Stream (load previous session)

Features:
- Input:
 - File: raw (no compression, only one file allowed) or compressed (7z, maximum compression)
 - Folder: always compressed (7z, maximum compression)
- Generation:
 - Uses current window size to determine frame capacity
 - Places data tiles, separators, margins, and ECC tiles
 - Per-frame CRC-32, global SHA-256 in metadata frame
- Navigation:
 - Automatic playback (configurable frame period)
 - Manual Next/Previous
 - Play/Pause toggle
- Save & Export:
 - Direct export during encoding
 - Export later from preview mode
 - Generate `encode_meta.txt` with encoding settings
- Resume:
 - Load previous encoding session
 - Continue from last frame

Proposed output layout:
- Frames directory, e.g.:
 - `frames/`
 - `frame_000000.png` (metadata)
 - `frame_000001.png` … `frame_NNNNNN.png`
 - `encode_meta.txt` (metadata + resume info)

Suggested `encode_meta.txt` format (key=value):
```
Version=1
EncodingCode=1
TileSize=8
ECCPer16=4
SeparatorEvery=8
FrameWidthPx=1920
FrameHeightPx=1080
TotalFrames=123
FirstUsefulFrameIndex=1
LastFailedFrameIndex=-1
PayloadType=7z|raw
OriginalName=MyFolder
OriginalLength=123456789
SHA256=<hex>
ColorMap=EmbeddedInFrame0
```

---

## Decoder (FluxRead)

**User Workflow:**

1. **Menu**: File → Start Reading
2. **Input Mode Selection**:
   
   **Option A: From Screen (Live Capture)**
   - Enter frame period (seconds) to sync with encoder
   - Click "Start Capturing"
   - Window minimizes to compact mode
   - Auto-positions away from capture area
   - Shows: "Capturing frame 15 of ??? ..."
   - Captures screenshots at specified interval
   - Saves to temp folder for processing
   
   **Option B: From Folder**
   - Select folder containing encoded frames
   - Optionally load progress.json or encode_meta.txt
   - Decodes all frames in folder
   - Shows progress in main window

3. **Decoding Pipeline**:
   - Parse frame headers
   - Extract metadata from Frame 0
   - Decode tiles with ECC correction
   - Verify CRC-32 per frame
   - Assemble payload
   - Verify SHA-256

4. **Output**:
   - Save reconstructed file/archive
   - Display integrity result (SHA-256 match)
   - Show detailed logs (ECC corrections, errors)
- Optional: Auto-extract 7z archive

5. **Resume**: Load previous decoder session

---

### Live Screenshot Capture Mode

**Purpose:** Decode frames being displayed on screen (e.g., from FluxCast streaming)

**Workflow:**
```
1. FluxCast on Computer A streams frames
2. FluxRead on Computer B captures screen
3. User enters frame period: "2.0 seconds"
4. Window minimizes and moves to corner
5. Captures screenshot every 2 seconds
6. Saves as capture_000001.png, capture_000002.png...
7. Decodes frames in real-time or batch
8. Reconstructs original file
```

**Window Behavior:**
- **Normal mode**: Full UI with controls
- **Capture mode**: Compact (300x100px) progress indicator
- **Auto-positioning**: Moves to top-right corner (outside typical capture area)
- **Progress**: "Capturing frame 42... Press ESC to stop"

---

### Folder Decode Mode

**Purpose:** Decode frames previously saved by FluxCast

**Workflow:**
```
1. User selects folder with encoded frames
2. System scans for frame_NNNNNN.png files
3. Loads metadata (progress.json or encode_meta.txt)
4. Decodes all frames
5. Shows progress: "Decoding frame 50/123..."
6. Saves reconstructed file
7. Displays integrity report
```

---

## Core Concepts (Updated)

- **Capture Modes**:
  - **Live**: Screenshots at regular intervals (sync with encoder)
  - **Folder**: Batch decode from saved frames
- **Window Modes**:
  - **Normal**: Full UI (800x600+)
  - **Compact**: Minimal progress (300x100) during screen capture
- **Sync Mechanism**: Frame period matching between encoder and decoder
- **Temp Storage**: Captured screenshots saved to temp before processing

---

## Error Handling

- Unsupported encoding code → stop with clear message.
- Parameter mismatch (e.g., tile size/ECC) → stop decoding.
- Insufficient ECC → list affected frames and allow partial export with warnings.
- IO issues (missing/unreadable images) → handle gracefully with logs and resume support.

---

## Roadmap / Nice to Have

- [ ] Batch decode multiple sets sequentially
- [ ] Health overlay (color-coded tiles: data/ECC/empty/corrected)
- [ ] Parallel decoding and memory-friendly streaming
- [ ] Automated7z extraction after successful verification
- [ ] Export/import session as a portable bundle

---

## Development Notes

`FluxCore` will expose:
- Compression:7z invoke (max compression) + raw passthrough
- Hashing: SHA-256 (full payload)
- ECC: Reed–Solomon with configurable symbols per16
- Framing: tile layout, separators, margins, ECC placement, header encode/decode
- Color map: deterministic256-color palette (excluding white) + encoder/decoder conversion
- CRC-32 per frame payload

UI (MAUI):
- `FluxCast`: source picker, tile size/ECC selectors, playback controls, save/resume
- `FluxRead`: source (screenshots/folder+TXT), sampling interval, progress overlay, logs, resume

---

## Contributing & License

- Contributions, issues, and feature requests are welcome.
- Add a `LICENSE` file (e.g., MIT) at the repository root.
