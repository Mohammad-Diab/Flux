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

- [x] **Frame 0 is an 8-color frame** (added after Phase 6): the metadata frame uses the 8 RGB cube corners (black/R/G/B/cyan/yellow/magenta/white) at 3 bits/tile, decoded by per-channel threshold — min pairwise distance 255, palette-independent, self-bootstrapping, keeps the embedded color map. Header+data tiles become 12× interleaved RS(255,127) codewords (`FrameEncoder.BuildMetadataFrame` / `FrameDecoder.DecodeMetadataFrame`, `CubeCornerColors`). Payload frames 1..N unchanged. White is a data color on frame 0 only. See "Future ideas: rugged payload mode".
- [x] 2.1 `Framing/FrameEncoder.cs` + `Framing/FrameTileMap.cs` — payload slice + header → RS encode (header ×3 copies + 53 payload codewords) → interleave → full 160×90 tile byte map incl. structural tiles (finders, timing, beacon parity = FrameId even/odd, pad)
  - **Test:** ✅ 13 tests — header copies decode back, data tiles decode to payload per level, partial last frame + CRC, beacon parity, real MetadataPayload round-trip. (Moved from planned `Encoding/` folder: that namespace shadows `System.Text.Encoding`.)
- [x] 2.2 `Imaging/FrameRenderer.cs` — tile map + ColorMap → 1312×752 PNG via SkiaSharp (8×8 rects, quiet zone, no antialiasing)
  - **Test:** ✅ 10 tests — canonical dims, quiet zone/finders/timing/beacon/pad pixel-exact, data+header tiles match palette exactly, frame 0 renders, deterministic output. Visual check of rendered frames confirmed structure. **Phase 2 gate: 147/147 green**

---

## Phase 3 — FluxCore v2: Decode Side (capture-tolerant)

- [x] 3.1 `Decoding/Homography.cs` — 4-point DLT, `Map(x,y)`
  - **Test:** ✅ 5 tests — identity/affine/projective, inverse consistency, degenerate rejection
- [x] 3.2 `Decoding/PaletteClassifier.cs` — nearest-palette by RGB distance + confidence (`d1>24` or `d1/d2>0.7` = low confidence)
  - **Test:** ✅ 4 tests — all 256 exact matches, perturbed-still-confident, white low-confidence, closest-pair-midpoint ambiguity flagged
- [x] 3.3 `Decoding/FiducialDetector.cs` — run-length 1:1:3:1:1 scan (±50%), vertical cross-check, clustering (sub-pixel centers from scanline averaging), extremal corner pick
  - **Test:** ✅ 6 tests — pristine within 2px of canonical centers, 0.75×/1.5× scale, +30/+45px offset on gray, blank fails cleanly, module size tracks scale. Plus `Decoding/LumaImage.cs` (Rec. 601 luma + bimodal threshold)
- [x] 3.4 `Decoding/TileSampler.cs` — tile centers through homography, average ~0.3×pitch neighborhood (exercised by all decoder tests)
- [x] 3.5 `Decoding/FrameDecoder.cs` + result types — full pipeline: binarize → fiducials → homography + orientation via timing (≥95% match, tries 180° flip) → sample → classify → **confidence gate (>8% low-conf → `CaptureUnstable`, no RS attempt)** → header (≥2 of 3 copies agree, or 1 plausible) → `SameFrameAsBefore`/`WrongFrame` short-circuit → de-interleave → 53× RS decode → CRC verify. Plus cheap `TryProbe` (fiducials + beacon + header only)
  - **Test:** ✅ 14 tests — pristine per level, partial payload, metadata frame, same/wrong-frame short-circuits, 1.25× scale, offset-on-gray, **180° rotation**, damaged-region ECC recovery with corrected-error diagnostics, blank-image failure, TryProbe beacon parity both ways. **Phase 3 gate: 176/176 green**

## 🚩 Milestone A — Golden Round-Trip (codec quality gate)

- [x] A.1 Integration test: payload → encode → PNG frames → decode every frame → assemble → SHA-256 match. Variants: multi-frame, 1-frame, exact-multiple-of-capacity, tiny, long unicode name, all ECC levels
  - ✅ 9 round-trip tests in `Integration/GoldenRoundTripTests.cs` (permanent codec regression gate)
- [x] A.2 Degradation matrix: scale {0.8, 1.0, 1.25} × JPEG quality {85, 75} × offset-on-gray. **Medium survives q85 at all scales, High survives q75 at all scales**; Medium@q75 and extreme JPEG (q30/q15) never silently corrupt; full multi-frame transfer through JPEG85+scale+offset round-trips
  - ✅ 11 tests in `Integration/DegradationMatrixTests.cs`. **Two design changes were required to pass:** (1) `ColorMap.Default` rebuilt as an evenly spaced 8×8×4 RGB lattice — the old palette packed 41 filler colors 1–3 RGB units apart, making ~16% of tiles unclassifiable under any lossy channel (ECC failed even at JPEG q85; this was almost certainly a v1 root-cause too). New minimum pairwise distance: 36 (pinned by test). Palette still fixed + embedded in frame 0, so no format change. (2) TileSampler switched to bilinear sub-pixel sampling (3×3 pattern at ±22% pitch) — integer-rounded window sampling broke down at 0.8× downscale (6.4px tiles). Also: the pre-ECC CaptureUnstable gate now runs *post-hoc* — decoder always attempts ECC, and reports CaptureUnstable only when recovery fails on a low-confidence capture.
- [x] A.3 Delete remaining v1 pipeline remnants
  - ✅ v1 pipeline files were already deleted in tasks 1.2/1.3; orphaned `EccException` removed here.
  - **Gate: ✅ 197/197 green.** Codec frozen; UI/optical work may begin.

---

## Phase 4 — FluxCore v2: Orchestration

- [x] 4.1 `Transfer/ContentSignature.cs` — file: SHA-256 over name‖length‖content (streamed); folder: SHA-256 over sorted (relPath‖length‖lastWriteUtc) + encode options; 16-hex-char session name
  - **Test:** ✅ 7 tests — stable across runs; changes with content/name/options/timestamps
- [x] 4.2 `Transfer/FluxEncodeService.cs` (+ EncodeOptions/Progress/SessionResult) — session folder `{root}/{signature}/` with `payload.dat` + `manifest.json` + `frames/frame_NNNNNN.png`; compress via `CompressionService`; chunk at 53×k; frame 0 at Max ECC; parallel render (temp-file + atomic move). **Resume: reuse verified payload.dat**, render only missing frames
  - **Test:** ✅ 6 tests — fresh session, full resume (0 rendered), single-missing-frame re-render, cancel-then-resume, changed-content fresh session, folder source decoded+extracted byte-identical. Found+fixed: CompressionService wrapped OperationCanceledException in CompressionException (cancellation now propagates properly)
- [x] 4.3 `Decoding/PayloadAssembler.cs` — accumulate per-frame payloads, completeness check, SHA-256 verify vs metadata, extract (7z decompress / raw write with sanitized name)
  - **Test:** ✅ 6 tests — out-of-order, duplicates ignored, missing-id reporting, tampered-frame SHA failure, inconsistent-frame rejection, raw extract. **Phase 4 gate: 216/216 green — FluxCore feature-complete**

---

## Phase 5 — WPF Shells (both apps)

- [x] 5.1 Gut FluxCast MAUI files; new csproj: `net8.0-windows`, `UseWPF`, `WinExe`, PerMonitorV2 `app.manifest`; CommunityToolkit.Mvvm + DI + Serilog file sink (`%LOCALAPPDATA%\Flux\logs\`)
- [x] 5.2 Same for FluxRead
- [x] 5.3 Recreate `Flux.sln` — FluxCore, FluxCore.Tests, FluxCast, FluxRead; AnyCPU only
  - **Test:** ✅ (pulled forward, after Phase 1, to clear 400+ VS errors from MAUI apps referencing deleted v1 types) — solution builds with 0 errors, 124/124 tests green. SharpCompress bumped 0.37.2 → 1.0.0 to clear vulnerability warning GHSA-6c8g-7p36-r338. Launch-to-shell check: verify in VS on next run.

---

## Phase 6 — FluxCast (WPF)

- [x] 6.1 Setup screen — file/folder pickers (`OpenFileDialog`/`OpenFolderDialog`), validation limits (10 GB file / 50 GB folder / 100k files, empty rejected) via `SourceValidator`, ECC level selector (default Medium), compress checkbox (locked on for folders)
- [x] 6.2 Session flow — `FluxEncodeService` (core) drives sessions in `%LOCALAPPDATA%\Flux\FluxCast\sessions\{signature}\`; resume inherent (reuses payload, renders only missing frames). `ShellViewModel` navigates Setup→Progress→Presenter
  - **Test:** ✅ encode created a 4-frame session on disk; core resume proven by Phase 4 tests
- [x] 6.3 Progress view — indeterminate bar during 7z, determinate during frame render, Cancel via CTS, error surface with Back
- [x] 6.4 Presenter view — **pixel-perfect**: `NearestNeighbor` + `UseLayoutRounding` + explicit `Image.Width/Height = PixelSize/DpiScale` (recomputed on `DpiChanged`); letterboxed on dark; fixed 110px bottom bar with large **◀ BACK / Frame N of T / NEXT ▶** never overlapped; `ResizeMode=NoResize` + window sized to fit frame at 1:1 while presenting; "don't move this window" hint; too-small warning banner; Left/Right key bindings
  - **Test:** ✅ launched, drove full flow via UIA (pick file → validate → encode → session on disk → presenter). Frame 0 renders crisp finders + interleaver streaks; Next advances to data-frame mosaic; Back enables. `CachedFrameProvider` prefetches ±3; `NullToCollapsedConverter` for error UI. Multi-scaling zoom check deferred to manual QA.

---

## Phase 7 — FluxRead: Folder-Decode Mode

- [x] 7.1 `FolderDecodeViewModel` + view — pick folder, enumerate `frame_??????.png`, decode each (frame 0 via `DecodeMetadataFrame`, rest via `Decode`), DataGrid of rows (file, id, status, detail with corrected-error count; failed rows tinted red), summary counts, progress bar
- [x] 7.2 `DecodePipelineService` (shared tail) — `DecodeFolderAsync` (order-tolerant, feeds `PayloadAssembler`) + `SaveAsync` → assemble → SHA verify → raw payloads to `SaveFileDialog(OriginalName)`, 7z payloads decompressed into `OpenFolderDialog`. `DialogService`, `ShellViewModel` (room for Phase 9 mode switch), converters, DI wiring
  - **Test:** ✅ covered by `FluxEncodeServiceTests.Encode_FolderSource_DecodesAndExtractsToIdenticalContent`

### FluxCast UX enhancements (user-requested, done)
- [x] Richer source info on selection — name, type, size, modified date, and estimated frame count (updates with ECC level / compress toggle)
- [x] Real compression percentage — 7z `-bsp1` progress parsed into `EncodeProgress.CompressionPercent`, shown as a determinate bar (indeterminate for the SharpCompress fallback)
- [x] Presenter frame navigation — First / Back / go-to-frame box / Next / Last

## 🚩 Milestone B — App-Level Round Trip

- [x] B.1 Encoded a 3-file folder (incl. 220 KB binary + nested file) via FluxCore encode → **23 frames** → FluxRead folder-decode
  - **Gate: ✅ PASS.** Pipeline decode+extract → all 3 files byte-identical by `Get-FileHash`. FluxRead GUI driven via UIA: grid shows frame 0 Metadata + 22 payload frames all Success/10123 bytes/0 corrected, "Complete… Ready to save". Also fixed 2 CA2014 stackalloc-in-loop warnings + an XML cref. 224/224 tests green on net10.

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

## Future ideas (post-v1, not scheduled)

- **Rugged payload mode** — reuse the frame-0 cube-corner scheme (8 colors, 3 bits/tile, per-channel threshold, min RGB distance 255) as an alternative *payload* encoding tier for truly awful channels, instead of the 256-color/1-byte-per-tile mode. Trades ~62% capacity for near-black/white robustness. Would be a per-transfer mode flag echoed in frame 0's metadata so the decoder picks the right payload scheme. Frame 0 itself already uses this scheme as of the 8-color metadata frame work.

## Accepted v1 limitations

- FluxRead has no cross-restart resume (a killed transfer restarts from frame 0)
- Windows-only (WPF + GDI capture + SendInput)
- Fixed grid/palette/scale — no adaptive sizing
