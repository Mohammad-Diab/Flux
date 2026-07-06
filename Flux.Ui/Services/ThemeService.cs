using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Flux.Ui.Services;

/// <summary>
/// Applies light/dark appearance by swapping the shared Color tokens. Theme brushes bind their
/// color to these tokens via DynamicResource, so replacing a token retints every brush — and every
/// StaticResource consumer — live, with no restart and no per-view changes.
/// </summary>
public sealed class ThemeService
{
    /// <summary>Whether the last applied appearance resolved to light.</summary>
    public bool IsLight { get; private set; }

    /// <summary>Applies the appearance for <paramref name="mode"/> (System reads the Windows setting).</summary>
    public void Apply(AppThemeMode mode)
    {
        bool light = mode switch
        {
            AppThemeMode.Light => true,
            AppThemeMode.Dark => false,
            _ => IsWindowsLight(),
        };
        IsLight = light;

        var r = Application.Current.Resources;
        r["TextPrimaryColor"] = C(light ? "#1A1D24" : "#EEF1F7");
        r["TextSecondaryColor"] = C(light ? "#5A6472" : "#9BA6B8");
        r["BorderColor"] = C(light ? "#D4D8E0" : "#2A3140");
        r["SurfaceColor"] = C(light ? "#FFFFFF" : "#181C27");
        r["SurfaceAltColor"] = C(light ? "#EAEDF3" : "#1F2434");
        r["ButtonHoverOverlayColor"] = C(light ? "#FF000000" : "#FFFFFFFF");
        r["HoverOverlayColor"] = C(light ? "#14000000" : "#14FFFFFF");
        r["HoverOverlayStrongColor"] = C(light ? "#1F000000" : "#22FFFFFF");
        r["AltRowColor"] = C(light ? "#0D000000" : "#0DFFFFFF");
        r["ScrollThumbColor"] = C(light ? "#C4CAD6" : "#3A4152");
        r["ScrollThumbHoverColor"] = C(light ? "#98A2B3" : "#5A657D");
        r["CaptionGlyphColor"] = C(light ? "#3A4150" : "#D6DAE3");
        r["BgColor1"] = C(light ? "#F6F7FA" : "#0C0E14");
        r["BgColor2"] = C(light ? "#EFF1F6" : "#12151F");
        r["BgColor3"] = C(light ? "#E9ECF3" : "#161A27");
        r["AmbientOpacity"] = light ? 0.5 : 1.0;
    }

    /// <summary>Reads the current Windows app-theme preference (true = light).</summary>
    public static bool IsWindowsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch
        {
            return false;
        }
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
