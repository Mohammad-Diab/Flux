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

## Known gaps found by the spike

- **Tall progress bar does not work.** WinUI's `ProgressBar` keeps a thin fixed-height track and
  ignores `Height`, so the readout bar renders as a hairline. The folder-decode readout needs a
  custom `ProgressBar` template (parts `DeterminateProgressBarIndicator` / `ProgressBarTrack`) or a
  width-driven `Border`. First task if the port proceeds.
- **No first-party DataGrid.** The Toolkit's is stale 7.x; the maintained option is third-party
  (`WinUI.TableView`). The read-only results grid needs neither — a header row over a `ListView`
  matches it, which is what the spike does. `ReceivedItemsView` should be checked the same way.
- Not yet attempted, and the expensive part of the real port: `TransitionHost` (genie/zoom needs
  Composition — WinUI has no `VisualBrush`), `RevealHost` (no `LayoutTransform`), `AmbientBackground`,
  `WindowChromeAnimator` (window open/close/minimise animation), `MiniCaptureWindow`, and the
  remaining ~700 lines of `Theme.xaml`.
- `FluxRead/Interop/` was not touched. The P/Invoke (GDI capture, SendInput, DPI, hotkey) should port
  as-is; only the pieces typed against WPF `Window` need adapting.
