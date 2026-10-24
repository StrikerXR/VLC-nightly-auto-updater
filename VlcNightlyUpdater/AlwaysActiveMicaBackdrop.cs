using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace VlcNightlyUpdater;

/// <summary>
/// Mica Alt that never switches to its washed-out "inactive" look. The stock MicaBackdrop follows window focus;
/// this one owns its configuration and keeps IsInputActive = true. Falls back to a plain window if Mica is unsupported.
/// </summary>
public sealed class AlwaysActiveMicaBackdrop : SystemBackdrop
{
    private MicaController? _controller;
    private SystemBackdropConfiguration? _config;
    private FrameworkElement? _root;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        if (!MicaController.IsSupported()) return;

        _config = new SystemBackdropConfiguration { IsInputActive = true };
        _root = xamlRoot.Content as FrameworkElement;
        if (_root is not null)
        {
            _config.Theme = ToTheme(_root.ActualTheme);
            _root.ActualThemeChanged += OnThemeChanged;
        }

        _controller = new MicaController { Kind = MicaKind.BaseAlt };
        _controller.SetSystemBackdropConfiguration(_config);
        _controller.AddSystemBackdropTarget(target);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        if (_root is not null) _root.ActualThemeChanged -= OnThemeChanged;
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null; _config = null; _root = null;
        base.OnTargetDisconnected(target);
    }

    private void OnThemeChanged(FrameworkElement sender, object args)
    {
        if (_config is not null) _config.Theme = ToTheme(sender.ActualTheme);
    }

    private static SystemBackdropTheme ToTheme(ElementTheme t) => t switch
    {
        ElementTheme.Dark => SystemBackdropTheme.Dark,
        ElementTheme.Light => SystemBackdropTheme.Light,
        _ => SystemBackdropTheme.Default
    };
}
