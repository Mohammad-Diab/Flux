# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Flux is

Flux moves a file/folder across a display-only channel by encoding it into error-corrected,
colored-tile frames (FFv3: RS(255,k) over GF(256), QR-style corner fiducials + homography
registration). Grid, tile size, and palette are per-transfer settings carried in frame 0 and
adopted by the receiver; 160×90 tiles at 8 px / 256 colors is the default and the frame-0
bootstrap anchor. Two WinUI 3 apps on .NET 10 over shared libraries:

- **FluxCast** — sender: file/folder → 7z compress → encode to frames → present one frame
  at a time with manual Back/Next navigation.
- **FluxRead** — receiver: folder-decode, plus live optical capture (screen region → decode →
  click Next → verify frame-id advanced → reassemble → SHA-256 verify → save).

These three projects were WPF until this branch, and the WinUI ports carried a `.WinUI` suffix while
both stacks coexisted. The WPF apps are now **removed** and the suffix dropped, so `FluxCast`,
`FluxRead` and `Flux.Ui` mean the WinUI ones — `master` still has the WPF originals under the same
names. A 0.10.0-beta Release publish of the WPF pair is kept in `dist/wpf-reference-0.10.0-beta/`
as the proven sender and receiver until these apps complete an optical transfer on real hardware;
it reads the same settings and session files, so a transfer can still cross between the two.

See README.md for the full frame-format spec, ECC-level table, and usage flow.

## Build & test

`Flux.sln` holds FluxCore, the tests, and the three WinUI projects. It **cannot be built by
`dotnet build`**: the Windows App SDK's PRI generation needs a task that ships with Visual Studio.
Run Restore and Build as two separate invocations from the repo root — a combined `/t:Restore,Build`
fails on a not-yet-restored tree, because the XAML compiler targets aren't loaded yet and every
`InitializeComponent` goes missing:

```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" `
    Flux.sln /t:Restore /p:Configuration=Debug /p:Platform=x64
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" `
    Flux.sln /t:Build /p:Configuration=Debug /p:Platform=x64
```

The core and the test loop need no Visual Studio:

```
dotnet build FluxCore/FluxCore.csproj -c Debug
dotnet test FluxCore.Tests/FluxCore.Tests.csproj
dotnet test FluxCore.Tests/FluxCore.Tests.csproj --filter "FullyQualifiedName~SomeTestName"
```

- Expect 365 passing tests; keep them green. The golden round-trip + degradation suite pins the
  codec — Medium ECC must survive JPEG q85, High q75, at 0.8×/1.0×/1.25× scale.
- Pre-existing/expected warnings: CompressionService CS8604 and a few FluxCore.Tests nullable
  warnings. Don't chase them; don't add new ones.
- Kill leftover FluxCast/FluxRead processes before rebuilding, or the Flux.Ui.dll
  copy fails (file locked).
- UI smoke-test procedure: launch the exe, screenshot the window (GetWindowRect + CopyFromScreen),
  then kill it. No synthesized clicks — never send global mouse clicks that could land on other
  windows. UIA Invoke/Select and cursor-hover-with-restore are acceptable; the real optical loop
  is user-driven.

## Solution structure

- **FluxCore** — codec/pipeline, deliberately UI- and Win32-free: `Framing/` (FrameFormat,
  parametric FrameLayout, header, encoder, metadata), `Ecc/`, `Imaging/` (PaletteGenerator,
  ColorMap, renderer, cube-corner colors), `Decoding/` (fiducials, homography, sampler, decoder,
  assembler), `Compression/`, `Hashing/`, `Transfer/` (content signature, encode service,
  capture-loop state machine).
- **FluxCore.Tests** — xUnit.
- **Flux.Ui** — the shared UI library, namespaces `Flux.Ui.*`. Holds the ONE
  `Theme.xaml`, the motion gate and curves (MotionSettings, MotionCurves), ReadoutBar,
  SlidingTabBar, TransitionHost, RevealHost, AmbientBackground, FluxDialog + MessageDialog,
  SettingsView, DialogService, `ITaskbarList3` progress, and `SettingsService` / `ByteFormat` /
  `TimeFormat` — settings.json keeps the WPF app's format, so the reference build in `dist/` reads
  and writes the same file.
- **FluxCast / FluxRead** — app-specific views/VMs/App.xaml.cs. Both shells navigate in
  code-behind: WinUI has no implicit DataTemplate-by-type, so WPF's ShellViewModel + typed templates
  have no twin. FluxRead's `Interop/` holds the Win32 capture, click, DPI, hotkey, region-overlay and
  window-placement helpers (Windows-specific code stays out of FluxCore).
  `FluxRead/PORT-NOTES.md` records every WinUI mechanism that replaced a WPF one, and the traps
  found on the way — read it before touching theming, dialogs, animation or interop.

## Key mechanisms

- **Live theming (no restart):** `ThemeDictionaries` + `ElementTheme` on the window root — no
  ThemeService and no token swapping. A second Window inherits neither the theme nor the dialogs,
  so copy `RequestedTheme` over. Both apps default to the System theme.
- **Motion gating:** MotionSettings (shared singleton, app resource `"MotionSettings"`) gates
  ALL animation — user preference AND `UISettings.AnimationsEnabled`. Animations are written in
  code with Storyboards; a layout property (Width/Height) needs `EnableDependentAnimation`, a
  `TranslateTransform` does not. The reduce-motion setting is a performance/accessibility feature;
  describe it only in those terms everywhere (code, commits, docs, UI strings).
- **Taskbar progress:** `Interop/TaskbarProgress` talks to `ITaskbarList3` directly (no
  TaskbarItemInfo in WinUI), attached once to the shell HWND.
- **Pixel-exact presentation:** the presenter sizes the image to
  `pixelWidth / XamlRoot.RasterizationScale`, recomputed on `SizeChanged`, with `Stretch="Fill"` so
  those bounds map the bitmap one device pixel per source pixel. It must not be `Stretch="None"`:
  that draws the bitmap at its natural DIP size and lets Width/Height merely clip, so above 100%
  scaling the frame comes out magnified and cut off. Never let WinUI resample a frame — it blurs the
  tile edges the decoder reads; too-large frames warn instead of scaling.
- **Format params ride in frame 0.** `FrameLayout` is parametric (grid, tile px, bits/tile);
  `PaletteGenerator.Generate(count, kind)` derives the palette so no colour list crosses the wire.
  Frame 0 is always `FrameLayout.Default` + cube corners — the bootstrap anchor, never user-driven.
  `TilePixelSize` is decode-irrelevant (the homography rescales), so capture fragility is gated at
  capture time (`CaptureTilePxFloor` filters the setup combos), not with more metadata.
- **Settings** persist to `%LOCALAPPDATA%\Flux\{FluxCast|FluxRead}\settings.json`. Encode sessions
  live under `%LOCALAPPDATA%\Flux\FluxCast\sessions\` in two levels —
  `{payloadKey}/payload.dat` + `renders/{renderKey}/frames/` — so re-rendering the same content at
  new settings reuses the compressed payload. The wire still carries one combined signature.
- **Capture-loop correctness:** the loop confirms a Next click worked by the decoded frame id
  incrementing — never a timer. Skipped frames are gap-recovered, not lost.
- **Dialogs:** every dialog is a `ContentDialog` drawing its own buttons (the stock
  Primary/Close buttons stretch across the command space and `DefaultButton` overwrites the style),
  shown through `DialogService`, which serialises them — WinUI allows one open at a time — on the
  active window's `XamlRoot`. Re-point that root while the shell is hidden behind the mini window,
  or the dialog never appears.
- External 7-Zip (`7z.exe`) is preferred for compression; falls back to bundled SharpCompress.
  Both apps declare Per-Monitor-V2 DPI awareness so screen coordinates are physical pixels.

## Conventions

- Minimal comments: at most one short line, only for a non-obvious "why". Clear names instead
  of comments (see CODING_GUIDELINES.md for naming).
- Keep FluxCore platform-neutral; UI/orchestration lives in the apps and Flux.Ui.
- Commit only when explicitly asked.
