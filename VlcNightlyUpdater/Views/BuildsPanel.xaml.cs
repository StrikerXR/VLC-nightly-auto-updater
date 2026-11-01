using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater.Views;

public sealed partial class BuildsPanel : UserControl
{
    private sealed record BuildItem(string Folder, string Label);

    private IReadOnlyList<string> _folders = [];
    private VlcArch _target = ArchInfo.Native;
    private bool _busy;

    /// <summary>The user picked a build folder (e.g. 20261021-0426) and pressed Install.</summary>
    public event EventHandler<string>? InstallRequested;

    public BuildsPanel()
    {
        InitializeComponent();
        RefreshCacheText();
    }

    public void SetBuilds(IEnumerable<string> folders, VlcArch target)
    {
        _folders = folders.Take(AppConfig.BuildsListed).ToList();
        _target = target;
        FillPicker();
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        BuildPicker.IsEnabled = !busy;
        InstallButton.IsEnabled = !busy && BuildPicker.SelectedItem is not null;
        RefreshCacheText();
    }

    /// <summary>Call after an install or cache change: updates the size text and the "cached" marks.</summary>
    public void Refresh()
    {
        RefreshCacheText();
        FillPicker();
    }

    private void RefreshCacheText()
    {
        var (count, bytes) = InstallerCache.Stats();
        CacheText.Text = count == 0 ? Strings.NoCache : Strings.CacheInfo(count, bytes);
        ClearButton.IsEnabled = count > 0 && !_busy;
    }

    private void FillPicker()
    {
        BuildPicker.ItemsSource = _folders.Select(f =>
        {
            var label = Format.Pretty(f) is { Length: > 0 } p ? $"{f}{Format.Dot}{p}" : f;
            if (InstallerCache.Has(f, _target)) label += Format.Dot + Strings.CachedMark;
            return new BuildItem(f, label);
        }).ToList();
    }

    private void BuildPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        InstallButton.IsEnabled = !_busy && BuildPicker.SelectedItem is not null;

    private void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (BuildPicker.SelectedItem is BuildItem item) InstallRequested?.Invoke(this, item.Folder);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        InstallerCache.Clear();
        Refresh();
    }
}
