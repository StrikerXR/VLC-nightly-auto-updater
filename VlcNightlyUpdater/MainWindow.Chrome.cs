using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace VlcNightlyUpdater;

/// <summary>Window look and size: title bar, Mica/acrylic backdrop, icon, start size. Sizes live in AppConfig.</summary>
public sealed partial class MainWindow
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private static bool IsDecember => DateTime.Now.Month == 12;

    private void SetupWindow()
    {
        // No classic title bar: our own header is the drag region, only the native min/close buttons remain.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var tb = AppWindow.TitleBar;
            tb.ButtonBackgroundColor = Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        SetBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter p) p.IsMaximizable = false; // resizable, but no maximize

        // December: the window and taskbar icon wears a Santa hat. Any other month: the normal icon.
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var icon = Path.Combine(assets, IsDecember ? "app-christmas.ico" : "app.ico");
        if (!File.Exists(icon)) icon = Path.Combine(assets, "app.ico");
        try { AppWindow.SetIcon(icon); } catch { }

        // Size in DIPs -> pixels (never bigger than the screen), then center on the current display.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int w = Math.Min((int)(AppConfig.WindowWidth * scale), area.Width - 40);
        int h = Math.Min((int)(AppConfig.WindowHeight * scale), area.Height - 40);
        AppWindow.Resize(new SizeInt32(w, h));
        AppWindow.Move(new PointInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2));
    }

    /// <summary>Mica on Windows 11, acrylic where Mica isn't available (Windows 10), plain themed background otherwise.</summary>
    private void SetBackdrop()
    {
        try
        {
            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                SystemBackdrop = new MicaBackdrop();
            else if (Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
                SystemBackdrop = new DesktopAcrylicBackdrop();
            else
                PlainBackground();
        }
        catch { PlainBackground(); }
    }

    private void PlainBackground() =>
        Root.Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
}
