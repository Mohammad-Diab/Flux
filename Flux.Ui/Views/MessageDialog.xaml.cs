using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Flux.Ui.Controls;

namespace Flux.Ui.Views;

/// <summary>
/// A themed modal replacement for the native message box. Shows a title, a message, and one or
/// two buttons; <see cref="Window.ShowDialog"/> returns true when the confirm button is chosen.
/// </summary>
public partial class MessageDialog : Window
{
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan OpenFadeDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(150);

    /// <summary>Creates a dialog. A null <paramref name="cancelText"/> shows only the confirm button.</summary>
    /// <param name="title">Heading text.</param>
    /// <param name="message">Body text.</param>
    /// <param name="confirmText">Label for the confirming button.</param>
    /// <param name="cancelText">Label for the dismissing button, or null for a single-button message.</param>
    /// <param name="destructive">When true the confirm button is styled as a destructive (red) action.</param>
    public MessageDialog(string title, string message, string confirmText, string? cancelText, bool destructive)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        ConfirmButton.Style = (Style)FindResource(destructive ? "DangerButton" : "PrimaryButton");

        if (cancelText is null)
            CancelButton.Visibility = Visibility.Collapsed;
        else
            CancelButton.Content = cancelText;

        Loaded += OnLoaded;
        // Escape dismisses through the same animated close (the buttons no longer auto-close the dialog).
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseWith(false);
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!MotionSettings.Current.AnimationsEnabled)
        {
            Opacity = 1;
            return;
        }

        var ease = MotionCurves.Pop;
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.88, 1, OpenDuration) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.88, 1, OpenDuration) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, OpenFadeDuration));
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => CloseWith(true);

    private void OnCancel(object sender, RoutedEventArgs e) => CloseWith(false);

    private bool _closing;

    // Shrink-and-fade out, then report the result (which closes the modal); instant when motion is off.
    private void CloseWith(bool result)
    {
        if (_closing)
            return;
        _closing = true;

        if (!MotionSettings.Current.AnimationsEnabled)
        {
            DialogResult = result;
            return;
        }

        var ease = MotionCurves.Exit;
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.92, CloseDuration) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.92, CloseDuration) { EasingFunction = ease });
        var fade = new DoubleAnimation(1, 0, CloseDuration) { EasingFunction = ease };
        fade.Completed += (_, _) => DialogResult = result;
        BeginAnimation(OpacityProperty, fade);
    }
}
