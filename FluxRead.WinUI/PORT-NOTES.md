# WinUI 3 port — spike findings

Branch `winui-port`. Target chosen 2026-07-26: **WinUI 3 (Windows App SDK), Windows-only.** The
backlog's cross-platform goal is explicitly deferred — WinUI 3 does not deliver it, and modernising
the Windows UI was the priority.

`FluxRead.WinUI` is a vertical spike, not a product: one screen (folder decode) end to end, to price
the real port. It is deliberately **not in `Flux.sln`**, so `dotnet build Flux.sln` and the 365
FluxCore tests keep working untouched.

## Proven working

- Unpackaged, self-contained WinUI 3 exe — launches like the WPF apps, no Windows App Runtime install.
- **FluxCore is reused verbatim.** So are `DecodePipelineService` and `PauseGate`, shared by source
  link — they never had a WPF dependency.
- Custom title bar via `ExtendsContentIntoTitleBar` + `SetTitleBar`.
- Live theming via `ThemeDictionaries` + `ElementTheme` — *simpler* than the WPF DynamicResource
  token-swap, and it needs no `ThemeService`.
- WPF template triggers → `VisualStateManager` states (see the button style: hover/press/disabled).
- Results table, decode with streaming rows, live readout, pause and cancel.
- File/folder pickers, which in an unpackaged app must each be bound to the window HWND.

## Build requirement (important)

`dotnet build` **cannot** build this project: the Windows App SDK's PRI generation needs
`Microsoft.Build.Packaging.Pri.Tasks.dll`, which ships with Visual Studio, not the .NET SDK. Use:

```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" `
    FluxRead.WinUI\FluxRead.WinUI.csproj /t:Restore,Build /p:Configuration=Debug /p:Platform=x64
```

## Theme port status

Ported and rendered: typography (Display/Heading/Brand/Subtle), Primary/Secondary/Danger buttons
with hover, press and disabled states, DangerGhostButton, Card, TextBox, ComboBox, ToggleSwitch, and
the tall readout bar. Verified against a throwaway gallery window, since removed.

Three WinUI mechanisms replace WPF ones, and all three are confirmed working:

| WPF | WinUI |
|---|---|
| `ControlTemplate.Triggers`, `MultiTrigger` | `VisualStateManager` states |
| `DynamicResource` colour-token swap + `ThemeService` | `ThemeDictionaries` + `ElementTheme` |
| Restyling a stock control's states by retemplating | Named "lightweight styling" brush overrides |

Not ported: `ScrollBar` (WinUI's overlay bars are already close), `CaptionButton` and `FluxWindow`
(the native title bar supersedes them), and the `DataGrid*` styles (no DataGrid — see below).

## Risk spikes — both resolved

**Pixel-exact frame presentation — PASSES, no fallback needed.** This was the risk that decided whether
FluxCast could move at all. WinUI's `Image` exposes no nearest-neighbour switch, but it does not need
one: size the image to `pixelSize / XamlRoot.RasterizationScale` and it lands on exact device pixels,
because WinUI's layout rounding is *device-pixel* aware. At 175% a 1312×752 frame laid out at a
fractional 749.714×429.714 DIP rendered as exactly 1312×752 device pixels, and a screen capture was
**byte-identical to the source PNG** across 109,938 sampled pixels — zero mismatches, zero channel
delta. No `SwapChainPanel`, Win2D or GDI child needed. Recompute on `SizeChanged`, as WPF did on
`DpiChanged`.

**Region-selector overlay — needs Win32, not XAML.** A WinUI window's backdrop is opaque and the
Windows App SDK 1.7 has no `TransparentBackdrop` (tried; the type does not exist), so a translucent
XAML `Grid` just composites over grey — verified: the overlay covered the desktop but you could not see
through it. `Interop/RegionSelectOverlay.cs` is the answer instead: a plain `WS_EX_LAYERED` Win32 window
spanning the virtual desktop, uniform alpha, marquee drawn with GDI, physical-pixel coordinates
throughout. Confirmed see-through over the full 2880×1620 desktop. It runs its own message loop, so it
must live on its own STA thread, not the WinUI dispatcher.
⚠️ The drag itself is user-driven and not yet exercised — synthesising a global mouse drag is off-limits
here, so verify the returned rectangle by hand before wiring the capture loop to it.

## Known gaps found by the spike

- **`ProgressBar` cannot be made tall.** Its thin track is baked into the template and it ignores
  both `Height` and a per-instance `ProgressBarTrackHeight` override — both were tried and neither
  worked. Solved by `Controls/ReadoutBar.cs`, a small custom control owning its own track and fill,
  with the readout drawn over it. Matches the WPF design.
- **No first-party DataGrid.** The Toolkit's is stale 7.x; the maintained option is third-party
  (`WinUI.TableView`). The read-only results grid needs neither — a header row over a `ListView`
  matches it, which is what the spike does. `ReceivedItemsView` should be checked the same way.
- **The pill tab style is only half of a tab bar.** `TabRadio`'s checked state is white text, which
  relies on the accent pill being drawn behind it by the strip — so a checked tab is invisible until
  `SlidingTabBar` is ported. Port the control and the style together.
- Not yet attempted, and the expensive part of the real port: `TransitionHost` (genie/zoom needs
  Composition — WinUI has no `VisualBrush`), `RevealHost` (no `LayoutTransform`), `AmbientBackground`,
  `WindowChromeAnimator` (window open/close/minimise animation), `MiniCaptureWindow`, and the
  remaining ~700 lines of `Theme.xaml`.
- `FluxRead/Interop/` was not touched. The P/Invoke (GDI capture, SendInput, DPI, hotkey) should port
  as-is; only the pieces typed against WPF `Window` need adapting.
