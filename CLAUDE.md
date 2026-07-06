# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Flux is

Flux moves a file/folder across a display-only channel by encoding it into error-corrected,
colored-tile frames (FFv2: 160×90 tiles, RS(255,k) over GF(256), QR-style corner fiducials +
homography registration). Two WPF apps on .NET 10 (`net10.0-windows`) over shared libraries:

- **FluxCast** — sender: file/folder → 7z compress → encode to FFv2 frames → present one frame
  at a time with manual Back/Next navigation.
- **FluxRead** — receiver: folder-decode, plus live optical capture (screen region → decode →
  click Next → verify frame-id advanced → reassemble → SHA-256 verify → save).

See README.md for the full frame-format spec, ECC-level table, and usage flow.

## Build & test

```
dotnet build Flux.sln -c Debug
dotnet test FluxCore.Tests/FluxCore.Tests.csproj
dotnet test FluxCore.Tests/FluxCore.Tests.csproj --filter "FullyQualifiedName~SomeTestName"
```

- Expect 235 passing tests; keep them green. The golden round-trip + degradation suite pins the
  codec — Medium ECC must survive JPEG q85, High q75, at 0.8×/1.0×/1.25× scale.
- Pre-existing/expected warnings: CompressionService CS8604 and a few FluxCore.Tests nullable
  warnings. Don't chase them; don't add new ones.
- Kill leftover FluxCast/FluxRead processes before rebuilding, or the Flux.Ui.dll copy fails
  (file locked).
- UI smoke-test procedure: launch the exe, screenshot the window (GetWindowRect + CopyFromScreen),
  then kill it. No synthesized clicks — never send global mouse clicks that could land on other
  windows. UIA Invoke/Select and cursor-hover-with-restore are acceptable; the real optical loop
  is user-driven.

## Solution structure

- **FluxCore** — codec/pipeline, deliberately UI- and Win32-free: `Framing/`, `Ecc/`,
  `Imaging/` (palette, renderer, cube-corner colors), `Decoding/` (fiducials, homography,
  sampler, decoder, assembler), `Compression/`, `Hashing/`, `Transfer/` (content signature,
  encode service, capture-loop state machine).
- **FluxCore.Tests** — xUnit.
- **Flux.Ui** — shared WPF library (`UseWPF`), namespaces `Flux.Ui.*`. Holds the ONE Theme.xaml,
  window-chrome/animation controls (WindowChromeAnimator, TransitionHost, Motion, NativeChrome,
  Win11Corners), MotionSettings, TaskbarProgress, WindowsThemeWatcher, ThemeService,
  SettingsService, and shared views (AmbientBackground, TitleBar, SettingsView,
  SettingsViewModel). Both apps reference it.
- **FluxCast / FluxRead** — app-specific views/VMs/App.xaml.cs only. FluxRead's `Interop/`
  holds the Win32 capture, click, DPI, hotkey, and window-placement helpers (Windows-specific
  code stays out of FluxCore).

## Key mechanisms

- **Live theming (no restart):** theme brushes bind their `Color` via DynamicResource to Color
  tokens; ThemeService swaps the tokens. Never mutate brushes in place — BAML freezes them.
  Both apps default to the System theme.
- **Motion gating:** MotionSettings (shared singleton, app resource `"MotionSettings"`) gates
  ALL animation — user preference AND `SystemParameters.ClientAreaAnimation`. Declarative
  animations use MultiTriggers on the attached `controls:Motion.Enabled` with an animated and
  an instant branch. The reduce-motion setting is a performance/accessibility feature; describe
  it only in those terms everywhere (code, commits, docs, UI strings).
- **Taskbar progress:** a TaskbarProgress singleton (app resource `"TaskbarProgress"`) drives
  each Window's TaskbarItemInfo.
- **Settings** persist to `%LOCALAPPDATA%\Flux\{FluxCast|FluxRead}\settings.json`. Encode
  sessions are resumable from `%LOCALAPPDATA%\Flux\FluxCast\sessions\{signature}\`.
- **Capture-loop correctness:** the loop confirms a Next click worked by the decoded frame id
  incrementing — never a timer. Skipped frames are gap-recovered, not lost.
- External 7-Zip (`7z.exe`) is preferred for compression; falls back to bundled SharpCompress.
  Both apps declare Per-Monitor-V2 DPI awareness so screen coordinates are physical pixels.

## Conventions

- Minimal comments: at most one short line, only for a non-obvious "why". Clear names instead
  of comments (see CODING_GUIDELINES.md for naming; its .NET8/MAUI header is outdated — the
  solution is .NET 10 WPF).
- Keep FluxCore platform-neutral; UI/orchestration lives in the apps and Flux.Ui.
- Commit only when explicitly asked.
