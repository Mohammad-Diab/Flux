using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluxRead.Interop;

namespace FluxRead.Views;

/// <summary>
/// Fullscreen transparent overlay spanning the whole virtual desktop. The user drags a
/// rectangle; on confirm it is converted to physical pixels and exposed as <see cref="Region"/>.
/// </summary>
public partial class RegionSelectorWindow : Window
{
    private Point _start;
    private bool _dragging;

    public RegionSelectorWindow()
    {
        InitializeComponent();

        // Cover the entire virtual screen in DIPs (WPF Left/Top/Width/Height are DIPs).
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKeyDown;
    }

    /// <summary>Gets the selected region in physical pixels, or null if cancelled.</summary>
    public Int32Rect? Region { get; private set; }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(RootCanvas);
        _dragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        SizeLabel.Visibility = Visibility.Visible;
        UpdateRect(_start);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragging)
            UpdateRect(e.GetPosition(RootCanvas));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        Confirm(e.GetPosition(RootCanvas));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Region = null;
            DialogResult = false;
        }
    }

    private void UpdateRect(Point current)
    {
        double x = Math.Min(_start.X, current.X);
        double y = Math.Min(_start.Y, current.Y);
        double w = Math.Abs(current.X - _start.X);
        double h = Math.Abs(current.Y - _start.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;

        Canvas.SetLeft(SizeLabel, x);
        Canvas.SetTop(SizeLabel, Math.Max(0, y - 22));
        SizeLabel.Text = $"{(int)w} × {(int)h}";
    }

    private void Confirm(Point end)
    {
        double x = Math.Min(_start.X, end.X);
        double y = Math.Min(_start.Y, end.Y);
        double w = Math.Abs(end.X - _start.X);
        double h = Math.Abs(end.Y - _start.Y);

        if (w < 8 || h < 8)
        {
            Region = null;
            DialogResult = false;
            return;
        }

        // The drag rect is in this window's DIP space; its top-left is at the virtual-screen
        // origin, so screen DIPs = window DIPs. Convert to physical pixels via the monitor DPI.
        var dipRect = new Rect(Left + x, Top + y, w, h);
        Region = DpiUtil.DipRectToPhysical(this, dipRect);
        DialogResult = true;
    }
}
