# WinUI 3 port — spike findings

Branch `winui-port`. Target chosen 2026-07-26: **WinUI 3 (Windows App SDK), Windows-only.** The
backlog's cross-platform goal is explicitly deferred — WinUI 3 does not deliver it, and modernising
the Windows UI was the priority.

It started as a vertical spike — one screen (folder decode) end to end, to price the real port. Both
apps are now ported, the WPF pair is removed from this branch, and `Flux.sln` holds FluxCore, the
tests and the three WinUI projects. These notes cover all three projects; they stay here rather than
at the repo root because that is where they were written.

## Proven working

- Unpackaged, self-contained WinUI 3 exe — launches like the WPF apps, no Windows App Runtime install.
- **FluxCore is reused verbatim.** So are `DecodePipelineService` and `PauseGate` — they never had a
  WPF dependency, so they were shared by source link during the port and moved here when it ended.
- Custom title bar via `ExtendsContentIntoTitleBar` + `SetTitleBar`.
- Live theming via `ThemeDictionaries` + `ElementTheme` — *simpler* than the WPF DynamicResource
  token-swap, and it needs no `ThemeService`.
- WPF template triggers → `VisualStateManager` states (see the button style: hover/press/disabled).
- Results table, decode with streaming rows, live readout, pause and cancel.
- File/folder pickers, which in an unpackaged app must each be bound to the window HWND.

## Build requirement (important)

`dotnet build` **cannot** build this project: the Windows App SDK's PRI generation needs
`Microsoft.Build.Packaging.Pri.Tasks.dll`, which ships with Visual Studio, not the .NET SDK. The
combined form below only works on an **already-restored** tree; on a fresh clone run `/t:Restore`
and `/t:Build` as two invocations, or the XAML compiler targets are not loaded yet and every
`InitializeComponent` goes missing. Whole solution: `Flux.sln` in place of the project path.

```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" `
    FluxRead.WinUI\FluxRead.WinUI.csproj /t:Restore,Build /p:Configuration=Debug /p:Platform=x64
```

## Theme port status

Ported and rendered: typography (Display/Heading/Brand/Subtle), Primary/Secondary/Danger buttons
with hover, press and disabled states, DangerGhostButton, Card, TextBox, ComboBox, ToggleSwitch, the
tall readout bar, and the ContentDialog surface (flat card, no top overlay or separator rule).
Verified against a throwaway gallery window, since removed.

These WinUI mechanisms replace WPF ones, and all are confirmed working:

| WPF | WinUI |
|---|---|
| `ControlTemplate.Triggers`, `MultiTrigger` | `VisualStateManager` states |
| `DynamicResource` colour-token swap + `ThemeService` | `ThemeDictionaries` + `ElementTheme` |
| Restyling a stock control's states by retemplating | Named "lightweight styling" brush overrides |
| `element.BeginAnimation(prop, animation)` | a `Storyboard` with `Storyboard.SetTarget`/`SetTargetProperty` |
| `AddHandler(ToggleButton.CheckedEvent, …)` on a parent | no such routed event — subscribe each child's `Checked` |
| `SetResourceReference` | read the resource and assign (no freezing to worry about) |
| modal `Window` + `ShowDialog()` | `ContentDialog.ShowAsync()` — needs a `XamlRoot`, and only one at a time |

⚠️ **Animating a layout property silently does nothing** unless the `DoubleAnimation` sets
`EnableDependentAnimation="True"`. The tab pill animates `Width`, so it needs the flag; `TranslateTransform.X`
does not, being composition-driven. This will bite every ported animation that touches size.

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
  (`WinUI.TableView`). Neither screen needed one: folder decode is a header row over a `ListView`, and
  `ReceivedItemsView` is cards over an `ItemsControl`, exactly as in WPF.
- Icon glyphs stay vector `Path`s throughout (Segoe MDL2 is tofu here). Note `Path` clips anything at
  negative coordinates, so keep the geometry inside a 0-origin box.
- ~~The pill tab style is only half of a tab bar~~ — **closed.** `Controls/SlidingTabBar.cs` is ported and
  the checked label reads correctly in both themes. Carries the WPF fix from `7f7c120`: the pill follows
  whichever tab is *checked*, not the event source.
- **The dim behind a `ContentDialog` is not `ContentDialogSmokeFill`.** In desktop WinUI the dialog is
  hosted in a popup whose overlay paints `SystemControlPageBackgroundMediumAltMediumBrush` directly;
  overriding the `*LightDismissOverlayBackground` keys that alias it does nothing (tried both the Popup
  and ContentDialog ones — a colour probe found the base brush). And the popup sits *outside* the window's
  `ElementTheme`, so it resolves Windows' theme: a dark dialog on a light Windows was washed out with
  white 60% (`#99FFFFFF`). Fixed by overriding the base brush theme-invariantly in `Theme.xaml`.
- Dialogs draw their own buttons rather than using `PrimaryButtonText`/`CloseButtonText`: the stock ones
  stretch across the command space, and setting `DefaultButton` overwrites the button `Style` with
  `AccentButtonStyle` from a visual state. With no button text the `NoneVisible` state collapses the
  command space, and Escape still dismisses — each dialog's choice property just stays at its default.
- ContentDialog's open/close scale-and-fade is a `VisualTransition` in the stock template, so the motion
  gate clears the template root's transitions in `OnApplyTemplate` (`Views/FluxDialog`).
- ~~Not yet attempted, the expensive part of the real port~~ — **all landed since:** `TransitionHost`
  (minus the genie), `RevealHost`, `AmbientBackground`, `MiniCaptureWindow` and the rest of
  `Theme.xaml`. `WindowChromeAnimator` turned out to be unnecessary (see Motion and polish).

## Interop

`NativeMethods`, `MouseClicker` and `OcrNextLocator` came over **unchanged** — no WPF in them, and an
unpackaged WinUI app gets the same WinRT projections, so `Windows.Media.Ocr` and `AsBuffer` work as
they did. The rest was rewritten:

- **`Int32Rect` → `Windows.Graphics.RectInt32`** throughout, including `RegionSelectOverlay`'s return.
  WPF's `DipToPhysical`/`DipRectToPhysical` are gone with the XAML region selector that needed them —
  the Win32 overlay already works in physical pixels.
- **`ScreenRegionCapture` is GDI `BitBlt` into a top-down 32-bit DIB**, not `Graphics.CopyFromScreen`, so
  the app needs no System.Drawing package. Two gotchas: ask for a *negative* `biHeight` or the rows come
  back bottom-up, and force the alpha byte to 255 — BitBlt copies whatever alpha was on screen (usually
  0), which a premultiplied `SKBitmap` reads back as black. Verified byte-identical to the WPF path.
- **`HotkeyListener` chains the window procedure.** WinUI has no `HwndSource.AddHook`; `SetWindowLongPtr`
  + `CallWindowProc` needs no comctl32-v6 manifest entry, unlike `SetWindowSubclass`. Keep the delegate
  alive and restore the old procedure on dispose.
- `WindowPlacement` still drives `SetWindowPos` directly — no need for `AppWindow.Move`, and it stays in
  physical pixels for the mixed-DPI case.
- Minimise/restore around a full-screen scan is `(Window.AppWindow.Presenter as OverlappedPresenter)`,
  WinUI's stand-in for `WindowState`.
- The capture loop runs on a worker thread but its stall and resume prompts are `ContentDialog`s, so each
  one hops back through `DispatcherQueue.TryEnqueue` wrapped in a `TaskCompletionSource`. WPF's
  `Dispatcher.InvokeAsync(...).Task.Unwrap()` has no direct equivalent.
- `ByteFormat`, `TimeFormat` and `SettingsService` live in `Flux.Ui.WinUI` but keep their old
  `Flux.Ui.*` namespaces, so the settings file stays byte-compatible with the WPF apps;
  `ShellViewModel.SessionRoot` became `Services/ReceptionPaths`, pointing at the same folder so a
  reception can move between the two stacks.

## Motion and polish

- **`TransitionHost` needs no Composition.** WPF snapshotted the outgoing page into a `VisualBrush`
  because it could; WinUI can simply keep both real pages on two layers and animate them. The only rule
  is that a page lives in one tree at a time, so it must leave the incoming presenter before the
  outgoing one can show it. **The genie is dropped** — its 120-strip warp would need a
  `CompositionVisualSurface` per strip, and the zoom-slide covers settings open/close well enough.
- `Timeline.DesiredFrameRate` does not exist, and the ambient drift does not miss it: it animates
  `TranslateTransform`, which runs on the compositor rather than the UI thread.
- No `TaskbarItemInfo`. `Interop/TaskbarProgress` calls `ITaskbarList3` directly — declare the
  `ITaskbarList`/`ITaskbarList2` methods first so the vtable lines up.
- `WindowChromeAnimator` and the custom `ScrollBar` styles are both **unnecessary**: WinUI never
  suppressed the OS window animations, and its overlay scrollbars already match the custom ones.

## Second windows (MiniCaptureWindow)

- A second `Window` inherits neither the shell's `ElementTheme` nor its dialogs. It needs its own
  `RequestedTheme` copied over, and while the shell is hidden the `DialogService` has to be re-pointed at
  the new window's `XamlRoot` — a `ContentDialog` on a hidden window's root never appears.
- `Topmost`, `ResizeMode` and `WindowState` are all `OverlappedPresenter` properties now
  (`IsAlwaysOnTop`, `IsResizable`, `Minimize`/`Restore`). Hiding a window is `AppWindow.Hide()`.
- **No animatable window size.** `AppWindow.MoveAndResize` takes physical pixels and nothing binds to it,
  so the collapse is tweened by hand on a 16 ms `DispatcherTimer` behind the motion gate. Resize from the
  pinned corner by recomputing the top from the fixed bottom edge, as the WPF version did.
- **A `Geometry` cannot be re-parented.** `XamlReader.Load`-ing a `Path` to lift its `Data` out and assign
  it elsewhere throws "Value does not fall within the expected range" — WinUI has no `Geometry.Parse`.
  Declare each icon state as its own `Path` in XAML and toggle `Visibility`.
- With `ExtendsContentIntoTitleBar`, the native caption buttons still draw over the content, so a custom
  close button doubles up. The mini window keeps the native close and hooks `AppWindow.Closing` to stop
  the transfer; header buttons need a right margin to clear the caption strip.
