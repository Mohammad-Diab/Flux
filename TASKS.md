# Flux v2 Rebuild — Task List

Flux is a one-way optical data bridge: **FluxCast** (WPF, runs inside a locked-down remote session) displays a file as colored-tile frames; **FluxRead** (WPF, runs locally) watches the screen region, decodes each frame, clicks Next itself, and reconstructs the file.

Tasks are dependency-ordered. Each task has a **Test** gate — do not start the next task until the gate passes. Milestones (🚩) are hard checkpoints.

**Branch:** `main` · **Archive of old MAUI code:** commit `f7dc70f` on `master`

---

## Phase 0 — Housekeeping

- [x] Archive commit of MAUI implementation (`f7dc70f`)
- [x] Create `main` branch
- [x] 0.1 Delete stray `Flux/` template project (not in sln, pure MAUI template)
- [x] 0.2 Remove dead `Platforms\*` folder items from `FluxCore.csproj`; delete empty `FluxCore/Platforms/` dirs
  - **Test:** ✅ 89 passed, 1 skipped, 0 failed. Fixed pre-existing bug found by the gate: `Compress7zExeAsync` archived the directory itself instead of its contents (nested extraction, disagreed with SharpCompress fallback). Skipped `TryDecode_WithCorruptedDataBeyondRepair_ReturnsFalse` — it demonstrates the known v1 ECC defect; class + test die in task 1.3.

---

## Phase 1 — FluxCore v2: Format Layer

Constants: 160×90 tiles, 8 px/tile, 16 px quiet zone → canonical PNG **1312×752**. Reserved tiles: 4× QR-style finder corners (8×8 blocks, 256), timing patterns (218), 3× header copies (144), beacon (16) → **13,515 data tiles = 53×255**, 251 white pad.

- [x] 1.1 `Framing/FrameFormat.cs` — all constants, tile role map (`TileRole GetRole(x,y)`), data-tile scan order, stride-53 interleaver (`tile t → codeword t%53, symbol t/53`), finder/timing/header/beacon coordinates
  - **Test:** ✅ 13 tests — accounting, golden hash `64073E66…B01A`, interleaver properties
- [x] 1.2 `Framing/FrameHeader.cs` — rewrite as 16-byte v2: `FormatVersion=2 | Flags(IsMetadata, EccLevel) | FrameId(u32) | TotalFrames(u32) | PayloadLength(u16) | PayloadCrc32(u32)`
  - **Test:** ✅ 15 tests. Old broken pipeline (FrameEncoder/Decoder/Frame*Service/FrameLayout) deleted here (was A.3) since the header rewrite broke it anyway
- [x] 1.3 `Ecc/EccLevel.cs` + `Ecc/ReedSolomonBlockCodec.cs` — real RS(255,k) via ZXing, ONE call per 255-symbol codeword. Levels: Low k=223, **Medium k=191 (default)**, High k=159, Max k=127. Header codec: RS(48,16). Delete `Ecc/ReedSolomonEcc.cs` + its tests
  - **Test:** ✅ 20 tests — incl. exactly-t recovers / t+1 fails per level, 400-tile contiguous burst recovers at Medium through the interleaver, header survives 16/48. Root cause of v1 silent corruption found: old code discarded ZXing's bool decode result
- [x] 1.4 `Framing/MetadataPayload.cs` — extend to v2 (Version=2): add EccLevel, TilePixelSize, GridW/H, TotalFrames, PayloadLength, ContentSignature; drop TileSize/EccPer16/SeparatorEvery/Algorithm
  - **Test:** ✅ 12 tests — v2 round-trips incl. unicode/long names, v1 rejected, geometry-mismatch detection, frame-0-fits-in-Max guarantee. **Phase 1 gate: 124/124 green**

---

## Phase 2 — FluxCore v2: Encode Side

- [x] 2.1 `Framing/FrameEncoder.cs` + `Framing/FrameTileMap.cs` — payload slice + header → RS encode (header ×3 copies + 53 payload codewords) → interleave → full 160×90 tile byte map incl. structural tiles (finders, timing, beacon parity = FrameId even/odd, pad)
  - **Test:** ✅ 13 tests — header copies decode back, data tiles decode to payload per level, partial last frame + CRC, beacon parity, real MetadataPayload round-trip. (Moved from planned `Encoding/` folder: that namespace shadows `System.Text.Encoding`.)
- [x] 2.2 `Imaging/FrameRenderer.cs` — tile map + ColorMap → 1312×752 PNG via SkiaSharp (8×8 rects, quiet zone, no antialiasing)
  - **Test:** ✅ 10 tests — canonical dims, quiet zone/finders/timing/beacon/pad pixel-exact, data+header tiles match palette exactly, frame 0 renders, deterministic output. Visual check of rendered frames confirmed structure. **Phase 2 gate: 147/147 green**

---

## Phase 3 — FluxCore v2: Decode Side (capture-tolerant)

- [ ] 3.1 `Decoding/Homography.cs` — 4-point DLT, `Map(x,y)`
  - **Test:** identity/affine/projective on known point sets; inverse consistency
- [ ] 3.2 `Decoding/PaletteClassifier.cs` — nearest-palette by squared RGB distance + confidence (`d1>24` or `d1/d2>0.7` = low confidence)
  - **Test:** exact match on pristine colors; low-confidence flags on perturbed near-black cluster (palette indices 215–255)
- [ ] 3.3 `Decoding/FiducialDetector.cs` — run-length 1:1:3:1:1 scan (±50%), vertical cross-check, clustering, sub-pixel centroid refine
  - **Test:** detects on pristine PNG; at 0.75× and 1.5× scale; with ±30 px offset padding; fails cleanly on a blank image
- [ ] 3.4 `Decoding/TileSampler.cs` — tile centers through homography, average ~0.3×pitch neighborhood
- [ ] 3.5 `Decoding/FrameDecoder.cs` + result types — full pipeline: binarize → fiducials → homography + orientation via timing (≥95% match) → sample → classify → **confidence gate (>8% low-conf → `CaptureUnstable`, no RS attempt)** → header (≥2 of 3 copies agree) → `SameFrameAsBefore`/`WrongFrame` short-circuit → de-interleave → 53× RS decode → CRC verify. Plus cheap `TryProbe` (fiducials + beacon + header only)
  - **Test:** decode Phase-2 pristine PNGs; correct status for wrong/same/corrupt frames; diagnostics populated (corrected-error counts, header-copy agreement)

## 🚩 Milestone A — Golden Round-Trip (codec quality gate)

- [ ] A.1 Integration test: real file → encode → PNG folder → decode every frame → assemble → SHA-256 match. Variants: 1-frame file, exact-multiple-of-capacity, tiny file, long unicode name
- [ ] A.2 Degradation matrix: re-render PNGs at scale {0.8, 1.0, 1.25} × JPEG quality {85, 75} × offset. **Medium must survive q85 at all scales**; beyond limits → clean `Undecodable`, never silent corruption (CRC catches all)
- [ ] A.3 Delete remaining v1 pipeline: `FrameEncoder/FrameDecoder/FrameEncodingService/FrameDecodingService/FrameLayout` (old Framing versions)
  - **Gate:** `dotnet test` fully green. No optical or UI work before this passes.

---

## Phase 4 — FluxCore v2: Orchestration

- [ ] 4.1 `Encoding/ContentSignature.cs` — file: SHA-256 over name‖length‖content (streamed); folder: SHA-256 over sorted (relPath‖length‖lastWriteUtc) + encode options; 16-hex-char session name
  - **Test:** stable across runs; changes when content/options change
- [ ] 4.2 `Encoding/FluxEncodeService.cs` — session folder `{root}/{signature}/` with `payload.7z` + `manifest.json` + `frames/frame_NNNNNN.png`; compress via existing `CompressionService`; chunk at 53×k; frame 0 at Max ECC; parallel render. **Resume: reuse existing payload.7z** (7z is not byte-deterministic), render only missing frames
  - **Test:** resume run reuses archive + renders only missing frames; cancellation cleans up
- [ ] 4.3 `Decoding/PayloadAssembler.cs` — accumulate per-frame payloads (HashSet of ids), completeness check, SHA-256 verify vs metadata, `DecompressAsync`
  - **Test:** out-of-order frames, duplicate frames, missing-frame reporting, SHA mismatch surfaces failed frame list

---

## Phase 5 — WPF Shells (both apps)

- [x] 5.1 Gut FluxCast MAUI files; new csproj: `net8.0-windows`, `UseWPF`, `WinExe`, PerMonitorV2 `app.manifest`; CommunityToolkit.Mvvm + DI + Serilog file sink (`%LOCALAPPDATA%\Flux\logs\`)
- [x] 5.2 Same for FluxRead
- [x] 5.3 Recreate `Flux.sln` — FluxCore, FluxCore.Tests, FluxCast, FluxRead; AnyCPU only
  - **Test:** ✅ (pulled forward, after Phase 1, to clear 400+ VS errors from MAUI apps referencing deleted v1 types) — solution builds with 0 errors, 124/124 tests green. SharpCompress bumped 0.37.2 → 1.0.0 to clear vulnerability warning GHSA-6c8g-7p36-r338. Launch-to-shell check: verify in VS on next run.

---

## Phase 6 — FluxCast (WPF)

- [ ] 6.1 Setup screen — file/folder pickers (`Microsoft.Win32.OpenFileDialog`/`OpenFolderDialog`), validation limits ported from old `InputValidationService` (10 GB file / 50 GB folder / 100k files, 7z warning), ECC level selector (default Medium)
- [ ] 6.2 `EncodeSessionService` — sessions in `%LOCALAPPDATA%\Flux\FluxCast\sessions\{signature}\`; completed session with all frames → jump to Presenter ("Resumed — N frames cached"); partial → re-encode
  - **Test:** encode, close app, reopen with same source → resumes without re-encoding
- [ ] 6.3 Progress view — indeterminate bar during 7z, determinate during frame render, Cancel via CTS
- [ ] 6.4 Presenter view — **pixel-perfect**: `NearestNeighbor` + `UseLayoutRounding` + explicit `Image.Width/Height = PixelSize/DpiScale` (recomputed on `DpiChanged`); letterboxed on dark background; fixed-height bottom bar with large **◀ BACK / Frame N of T / NEXT ▶** never overlapped by the frame; `ResizeMode=NoResize` while presenting + "don't move this window" hint; no animations on Next
  - **Test:** screenshot at 100%/125%/150% display scaling — tile edges are crisp 8-px blocks (zoom in and check)

---

## Phase 7 — FluxRead: Folder-Decode Mode

- [ ] 7.1 `FolderDecodeViewModel` + view — pick folder, enumerate `frame_??????.png`, decode each, results grid (frame id, status, diagnostics), summary counts
- [ ] 7.2 `DecodePipelineService.FinalizeAsync` (shared tail) — assemble → SHA verify → decompress → SaveFileDialog seeded with `OriginalName` (folder payloads → OpenFolderDialog)
  - **Test:** unit-level with Milestone-A fixtures

## 🚩 Milestone B — App-Level Round Trip

- [ ] B.1 Encode a real folder in FluxCast → point FluxRead folder-decode at the frames → output byte-identical (`Get-FileHash` both sides)
  - **Gate:** passes with a multi-frame (>10 frames) payload. No optical work before this.

---

## Phase 8 — FluxRead: Interop Layer

All in `FluxRead/Interop/`, each exercisable from a hidden dev panel:

- [ ] 8.1 `DpiUtil` — DIP↔physical px, per-monitor (`MonitorFromPoint` + `GetDpiForMonitor`)
- [ ] 8.2 `ScreenRegionCapture` — `Graphics.CopyFromScreen` → raw BGRA → SKBitmap (no PNG in the loop; diagnostic dump behind a toggle)
  - **Test:** dev-panel capture of a chosen region shows correct thumbnail on a 125% scaled monitor
- [ ] 8.3 `MouseClicker` — `SendInput` absolute + `VIRTUALDESK` normalized coords, ~30 ms down/up gap, save/restore cursor
  - **Test:** dev-panel click at a chosen point hits Paint/Notepad button on both monitors
- [ ] 8.4 `HotkeyListener` — `RegisterHotKey(F8)` + `WM_HOTKEY` hook
- [ ] 8.5 `WindowPlacement.EnsureOutsideRegion` — move own window off the capture region; re-check on `LocationChanged`; optional `WDA_EXCLUDEFROMCAPTURE`

---

## Phase 9 — FluxRead: Live Optical Mode

- [ ] 9.1 `RegionSelectorWindow` — fullscreen transparent Topmost overlay spanning the virtual screen, drag rectangle, Esc cancels; result stored as physical-px RECT
- [ ] 9.2 Next-button calibration — "Hover over FluxCast's NEXT button, press F8" → `GetCursorPos` → stored point
- [ ] 9.3 `CaptureLoopService` state machine — `WaitingForFrame0 → Decoding → ClickingNext → WaitingForAdvance → … → Reassembling → Complete` + `Stalled/Failed/Cancelled`. Rules:
  - **Stability precondition:** two consecutive pixel-identical captures before any decode (palette-risk mitigation)
  - Advance confirmation = decoded frame id incremented (via `TryProbe` beacon+header first). Never a timer.
  - `SameFrameAsBefore` after K=8 polls → re-click; max R=3 re-clicks → **Stalled** (banner: Retry / Recalibrate / Abort — never spins forever)
  - Unexpected-but-missing frame id (user touched FluxCast) → accept + resync; completeness = HashSet
  - **Test:** unit-test the state machine with a scripted fake decoder (advance, stall, out-of-order, unstable-capture sequences)
- [ ] 9.4 Live UI — big state label, frame N/T, retries, last-capture thumbnail, capped scrolling log, Stop button

## 🚩 Milestone C — Optical Loop

- [ ] C.1 **Local:** FluxCast + FluxRead side-by-side on one machine, region over the FluxCast window — complete a multi-frame transfer purely optically (capture + synthesized clicks), SHA verified
- [ ] C.2 **Real:** same through an actual RDP session — **this is the v1 acceptance test**

---

## Phase 10 — Polish

- [ ] 10.1 `TransferReport` summary (frames, retries, stalls, elapsed, throughput)
- [ ] 10.2 Rewrite README.md for v2 (FFv2 format spec, usage guide)
- [ ] 10.3 Dead-code sweep, final `dotnet test`, tag `v1.0`

---

## Accepted v1 limitations

- FluxRead has no cross-restart resume (a killed transfer restarts from frame 0)
- Windows-only (WPF + GDI capture + SendInput)
- Fixed grid/palette/scale — no adaptive sizing
