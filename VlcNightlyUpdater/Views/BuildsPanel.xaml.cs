using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;
using Windows.ApplicationModel.DataTransfer;

namespace VlcNightlyUpdater.Views;

public sealed partial class BuildsPanel : UserControl
{
    private sealed record BuildItem(string Folder, string Label);

    private IReadOnlyList<string> _folders = [];
    private VlcArch _target = ArchInfo.Native;
    private bool _busy;

    /// <summary>The user picked a build folder (e.g. 20261021-0426) and pressed Install.</summary>
    public event EventHandler<string>? InstallRequested;

    /// <summary>"Stop skipping" was pressed.</summary>
    public event EventHandler? UnskipRequested;

    public BuildsPanel()
    {
        InitializeComponent();
        SkippedText.Text = Strings.NoSkipped;
        RefreshCacheText();
    }

    public void SetBuilds(IEnumerable<string> folders, VlcArch target)
    {
        _folders = folders.Take(AppConfig.BuildsListed).ToList();
        _target = target;
        FillPicker();
    }

    /// <summary>Fills the "Recent installs" card (newest first).</summary>
    public void SetHistory(IEnumerable<HistoryEntry> entries)
    {
        var lines = entries.Select(Strings.HistoryLine).ToList();
        HistoryText.Text = lines.Count == 0 ? Strings.HistoryNone : string.Join("\n", lines);
    }

    /// <summary>Shows which build is skipped (null = none) and the button to undo that.</summary>
    public void SetSkipped(string? folder)
    {
        SkippedText.Text = folder is null ? Strings.NoSkipped : Strings.SkippedRow(folder);
        UnskipButton.Visibility = folder is null ? Visibility.Collapsed : Visibility.Visible;
        UnskipButton.IsEnabled = !_busy;
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        UnskipButton.IsEnabled = !busy;
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

    private void BuildPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var any = BuildPicker.SelectedItem is not null;
        InstallButton.IsEnabled = !_busy && any;
        OpenBuildLink.IsEnabled = any;
        CopyBuildLink.IsEnabled = any;
    }

    private void OpenBuildLink_Click(object sender, RoutedEventArgs e)
    {
        if (BuildPicker.SelectedItem is not BuildItem item) return;
        if (!AppConfig.Channels.TryGetValue(_target, out var channels) || channels.Length == 0) return;
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri($"{AppConfig.NightlyRoot}{channels[0]}/{item.Folder}/"));
    }

    private async void CopyBuildLink_Click(object sender, RoutedEventArgs e)
    {
        if (BuildPicker.SelectedItem is not BuildItem item) return;
        try
        {
            var package = new DataPackage();
            package.SetText(item.Folder);
            Clipboard.SetContent(package);
        }
        catch { return; }

        CopyBuildLink.Content = Strings.Copied;
        await Task.Delay(1500);
        CopyBuildLink.Content = "Copy build name";
    }

    private void UnskipButton_Click(object sender, RoutedEventArgs e) => UnskipRequested?.Invoke(this, EventArgs.Empty);

    private void OpenCacheButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(InstallerCache.Root);
            VlcProcess.OpenFolder(InstallerCache.Root);
        }
        catch { /* folder can't be opened: nothing useful to say */ }
    }

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
