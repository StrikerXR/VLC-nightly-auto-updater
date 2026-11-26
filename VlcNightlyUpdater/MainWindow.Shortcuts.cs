using Microsoft.UI.Xaml.Input;
using VlcNightlyUpdater.Services;
using Windows.System;

namespace VlcNightlyUpdater;

/// <summary>
/// Keyboard shortcuts (F5 lives in MainWindow.xaml.cs). To add one: add a line to <see cref="SetupShortcuts"/>.
///   Ctrl+1 / 2 / 3   Changes / Settings / Builds      Ctrl+R   Check again
///   Ctrl+F           filter the commits               Ctrl+L   Launch VLC
///   Ctrl+,           Settings
/// </summary>
public sealed partial class MainWindow
{
    private void Shortcut(VirtualKey key, Action act)
    {
        var a = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
        a.Invoked += (_, args) =>
        {
            args.Handled = true;
            act();
        };
        Root.KeyboardAccelerators.Add(a);
    }

    private void SelectTab(int index)
    {
        if (index >= 0 && index < Tabs.Items.Count) Tabs.SelectedItem = Tabs.Items[index];
    }

    private void SetupShortcuts()
    {
        Shortcut(VirtualKey.Number1, () => SelectTab(0));
        Shortcut(VirtualKey.Number2, () => SelectTab(1));
        Shortcut(VirtualKey.Number3, () => SelectTab(2));
        Shortcut((VirtualKey)188, () => SelectTab(1)); // Ctrl+, (the comma key)
        Shortcut(VirtualKey.R, async () =>
        {
            if (!_busy) await RunGuardedAsync(CheckFlowAsync);
        });
        Shortcut(VirtualKey.F, () =>
        {
            SelectTab(0);
            Changes.FocusFilter();
        });
        Shortcut(VirtualKey.L, () =>
        {
            if (!_busy && VlcLocator.FindVlcExe() is not null) VlcProcess.Launch();
        });
    }
}
