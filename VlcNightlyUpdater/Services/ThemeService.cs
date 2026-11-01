using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace VlcNightlyUpdater.Services;

public readonly record struct AccentOption(string Key, string Name, Color Color, Color PreviewColor);

/// <summary>
/// Accent colors and dynamic theme switching. Updates VnuBrandBrush, phase working/download brushes,
/// and native WinUI button accents so the entire app updates immediately when the user chooses a color.
/// </summary>
public static class ThemeService
{
    private static UISettings? _uiSettings;

    public static readonly Color BrandOrange = Color.FromArgb(0xFF, 0xFF, 0x88, 0x00);

    public static readonly IReadOnlyList<AccentOption> Options = new[]
    {
        new AccentOption("orange", "Orange (VLC default)", BrandOrange, BrandOrange),
        new AccentOption("system", "Windows accent", Colors.Transparent, GetSystemAccentColor()),
        new AccentOption("blue", "Blue", Color.FromArgb(0xFF, 0x00, 0x78, 0xD4), Color.FromArgb(0xFF, 0x00, 0x78, 0xD4)),
        new AccentOption("teal", "Teal", Color.FromArgb(0xFF, 0x00, 0x82, 0x72), Color.FromArgb(0xFF, 0x00, 0x82, 0x72)),
        new AccentOption("green", "Green", Color.FromArgb(0xFF, 0x10, 0x7C, 0x41), Color.FromArgb(0xFF, 0x10, 0x7C, 0x41)),
        new AccentOption("purple", "Purple", Color.FromArgb(0xFF, 0x87, 0x64, 0xB8), Color.FromArgb(0xFF, 0x87, 0x64, 0xB8)),
        new AccentOption("pink", "Pink", Color.FromArgb(0xFF, 0xE3, 0x00, 0x8C), Color.FromArgb(0xFF, 0xE3, 0x00, 0x8C)),
        new AccentOption("red", "Red", Color.FromArgb(0xFF, 0xE8, 0x11, 0x23), Color.FromArgb(0xFF, 0xE8, 0x11, 0x23)),
        new AccentOption("amber", "Amber", Color.FromArgb(0xFF, 0xFF, 0xB9, 0x00), Color.FromArgb(0xFF, 0xFF, 0xB9, 0x00)),
    };

    public static Color GetSystemAccentColor()
    {
        try
        {
            _uiSettings ??= new UISettings();
            return _uiSettings.GetColorValue(UIColorType.Accent);
        }
        catch
        {
            return BrandOrange;
        }
    }

    public static Color ResolveColor(string key)
    {
        if (string.Equals(key, "system", StringComparison.OrdinalIgnoreCase))
            return GetSystemAccentColor();

        var opt = Options.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));
        return opt.Key is not null ? opt.Color : BrandOrange;
    }

    public static void ApplyAccent(string key)
    {
        var color = ResolveColor(key);
        var tint = Color.FromArgb(0x33, color.R, color.G, color.B);

        SetBrush("VnuBrandBrush", color);
        SetBrush("VnuBrandTintBrush", tint);
        SetBrush("VnuPhaseWorkingBrush", color);
        SetBrush("VnuPhaseWorkingTintBrush", tint);
        SetBrush("VnuPhaseDownloadBrush", color);
        SetBrush("VnuPhaseDownloadTintBrush", tint);

        // WinUI native accent button resources
        Application.Current.Resources["SystemAccentColor"] = color;
        SetBrush("AccentFillColorDefaultBrush", color);
        SetBrush("AccentButtonBackground", color);

        var hover = Color.FromArgb(0xE6, color.R, color.G, color.B);
        var pressed = Color.FromArgb(0xCC, color.R, color.G, color.B);
        SetBrush("AccentFillColorSecondaryBrush", hover);
        SetBrush("AccentFillColorTertiaryBrush", pressed);
        SetBrush("AccentButtonBackgroundPointerOver", hover);
        SetBrush("AccentButtonBackgroundPressed", pressed);
    }

    private static void SetBrush(string key, Color color)
    {
        if (Application.Current.Resources.TryGetValue(key, out var obj) && obj is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        else
        {
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
    }

    public static void ListenToSystemAccent(Action onChanged)
    {
        try
        {
            _uiSettings ??= new UISettings();
            _uiSettings.ColorValuesChanged += (_, _) => onChanged();
        }
        catch { }
    }
}
