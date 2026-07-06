using System.Windows;
using System.Windows.Interop;
using FluxRead.Interop;
using FluxRead.Services;

namespace FluxRead.Views;

/// <summary>
/// Diagnostic window that exercises each interop helper by hand. Logic lives in code-behind
/// because these helpers operate on the real screen, cursor, and window handle — there is no
/// meaningful view model to unit-test.
/// </summary>
public partial class InteropDevWindow : Window
{
    private static InteropDevWindow? _open;

    private readonly ScreenRegionCapture _capture = new();
    private HotkeyListener? _hotkey;
    private bool _excluded;

    /// <summary>Opens the single dev-tools window, or refocuses it if already open.</summary>
    public static void ShowSingle(Window? owner)
    {
        if (_open is null)
        {
            _open = new InteropDevWindow { Owner = owner };
            _open.Closed += (_, _) => _open = null;
            _open.Show();
            return;
        }

        if (_open.WindowState == WindowState.Minimized)
            _open.WindowState = WindowState.Normal;
        _open.Activate();
    }

    public InteropDevWindow()
    {
        InitializeComponent();
        Flux.Ui.Controls.FluxWindowChrome.Attach(this, RootContent);
        Closed += (_, _) => _hotkey?.Dispose();
    }

    private async void OnReadDpi(object sender, RoutedEventArgs e)
    {
        DpiResult.Text = "Move the cursor to the target monitor…";
        await Task.Delay(2000);
        NativeMethods.GetCursorPos(out var pos);
        double scale = DpiUtil.GetScaleForPhysicalPoint(pos.X, pos.Y);
        DpiResult.Text = $"Cursor ({pos.X},{pos.Y}) → scale {scale:0.##}× ({scale * 96:0} DPI)";
    }

    private void OnCapture(object sender, RoutedEventArgs e)
    {
        try
        {
            var region = new Int32Rect(ParseInt(CapX), ParseInt(CapY), ParseInt(CapW), ParseInt(CapH));
            using var bitmap = _capture.Capture(region);
            CapImage.Source = BitmapConverter.ToBitmapSource(bitmap, quality: 100);
            CapResult.Text = $"Captured {bitmap.Width}×{bitmap.Height}px from ({region.X},{region.Y}).";
        }
        catch (Exception ex)
        {
            CapResult.Text = $"Capture failed: {ex.Message}";
        }
    }

    private async void OnClick(object sender, RoutedEventArgs e)
    {
        int x = ParseInt(ClickX), y = ParseInt(ClickY);
        for (int i = 3; i >= 1; i--)
        {
            ClickResult.Text = $"Clicking ({x},{y}) in {i}…";
            await Task.Delay(1000);
        }

        await Task.Run(() => MouseClicker.ClickAt(x, y));
        ClickResult.Text = $"Clicked ({x},{y}); cursor restored.";
    }

    private void OnToggleHotkey(object sender, RoutedEventArgs e)
    {
        if (_hotkey is null)
        {
            _hotkey = new HotkeyListener(this);
            _hotkey.Pressed += OnHotkeyPressed;
        }

        if (HotkeyButton.Content is "Arm F8")
        {
            _hotkey.Arm();
            HotkeyButton.Content = "Disarm F8";
            HotkeyResult.Text = "Armed. Hover anywhere and press F8.";
        }
        else
        {
            _hotkey.Disarm();
            HotkeyButton.Content = "Arm F8";
            HotkeyResult.Text = "Disarmed.";
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        NativeMethods.GetCursorPos(out var pos);
        HotkeyResult.Text = $"F8 captured cursor at ({pos.X},{pos.Y}).";
    }

    private void OnEnsureOutside(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var region = new Int32Rect(ParseInt(CapX), ParseInt(CapY), ParseInt(CapW), ParseInt(CapH));
        bool moved = WindowPlacement.EnsureOutsideRegion(handle, region);
        PlacementResult.Text = moved ? "Window moved off the region." : "Window already clear of the region.";
    }

    private void OnToggleExclude(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _excluded = !_excluded;
        WindowPlacement.SetExcludeFromCapture(handle, _excluded);
        PlacementResult.Text = _excluded
            ? "Exclude-from-capture ON (window is black in captures)."
            : "Exclude-from-capture OFF.";
    }

    private static int ParseInt(System.Windows.Controls.TextBox box) =>
        int.TryParse(box.Text, out int value) ? value : 0;
}
