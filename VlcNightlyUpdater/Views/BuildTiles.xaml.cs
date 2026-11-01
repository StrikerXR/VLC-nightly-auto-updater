using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater.Views;

public sealed partial class BuildTiles : UserControl
{
    public BuildTiles() => InitializeComponent();

    public void SetInstalled(string? folder, VlcArch? arch)
    {
        Installed.Value = folder ?? "\u2014";
        Installed.Sub = Format.Join(Format.Pretty(folder), arch?.Label());
    }

    public void SetLatest(BuildInfo? latest)
    {
        if (latest is null) return;
        Latest.Value = latest.Folder;
        Latest.Sub = Format.Join(Format.Pretty(latest.Folder), latest.Arch.Label());
    }

    /// <summary>value: "47", "0" or "\u2014"; caption: the muted line under it.</summary>
    public void SetBehind(string value, string caption)
    {
        Behind.Value = value;
        Behind.Sub = caption;
    }
}
