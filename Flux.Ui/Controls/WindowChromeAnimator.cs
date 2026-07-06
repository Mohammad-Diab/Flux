using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Flux.Ui.Controls;

/// <summary>
/// All window-motion for the borderless (WindowChrome) windows in one place. Because
/// <c>WindowStyle="None"</c> suppresses the native minimize/maximize/close animations, we supply
/// our own: maximize/restore settle, an animated minimize (fade + shrink out, then minimize),
/// restore-from-minimize (its reverse: fade + grow in), and an animated close (fade + shrink out
/// via the Closing event). Minimize is intercepted for both the caption button and the taskbar
/// (WM_SYSCOMMAND) so it animates however it's triggered. Honors <see cref="MotionSettings"/>.
/// </summary>
public static class WindowChromeAnimator
{
    private const int WmSysCommand = 0x0112;
    private const int ScMinimize = 0xF020;

    private sealed class State
    {
        public ScaleTransform Scale = null!;
        public WindowState Prev;
        public bool AllowClose;
        public bool Animating;
    }

    private static readonly ConditionalWeakTable<Window, State> States = new();

    /// <summary>
    /// Attaches window-motion to <paramref name="window"/>, scaling <paramref name="content"/>
    /// (typically the window's root panel) and fading the window as a whole.
    /// </summary>
    public static void Attach(Window window, FrameworkElement content)
    {
        var scale = new ScaleTransform(1, 1);
        content.RenderTransform = scale;
        content.RenderTransformOrigin = new Point(0.5, 0.5);
        var state = new State { Scale = scale, Prev = window.WindowState };
        States.AddOrUpdate(window, state);

        window.StateChanged += (_, _) => OnStateChanged(window, state);
        window.Closing += (_, e) =>
        {
            if (state.AllowClose || !MotionSettings.Current.AnimationsEnabled)
                return;
            e.Cancel = true;
            AnimateOut(window, state, () => { state.AllowClose = true; window.Close(); });
        };
        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource source)
                source.AddHook(Hook);
        };

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmSysCommand && ((int)wParam.ToInt64() & 0xFFF0) == ScMinimize)
            {
                handled = true;
                Minimize(window);
            }

            return IntPtr.Zero;
        }
    }

    /// <summary>Minimizes <paramref name="window"/> with a fade-and-shrink animation.</summary>
    public static void Minimize(Window window)
    {
        if (!MotionSettings.Current.AnimationsEnabled || !States.TryGetValue(window, out var state))
        {
            window.WindowState = WindowState.Minimized;
            return;
        }

        AnimateOut(window, state, () =>
        {
            window.WindowState = WindowState.Minimized;
            state.Animating = false;
        });
    }

    private static void OnStateChanged(Window window, State state)
    {
        var current = window.WindowState;
        if (current == WindowState.Minimized || !MotionSettings.Current.AnimationsEnabled)
        {
            state.Prev = current;
            return;
        }

        if (state.Prev == WindowState.Minimized)
        {
            // Restore from minimize: the reverse of minimize — fade + grow back in.
            AnimateIn(window, state);
        }
        else if (current == WindowState.Maximized)
        {
            SettleScale(state, 0.96);
        }
        else
        {
            // Restore from maximize: content settles down (reverse of the maximize grow).
            SettleScale(state, 1.04);
        }

        state.Prev = current;
    }

    private static void AnimateOut(Window window, State state, Action done)
    {
        state.Animating = true;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(window.Opacity, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
        fade.Completed += (_, _) => done();
        var shrink = new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
        state.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        state.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        window.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static void AnimateIn(Window window, State state)
    {
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };
        state.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        state.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        window.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    private static void SettleScale(State state, double from)
    {
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var settle = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
        state.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
        state.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
    }
}
