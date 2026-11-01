namespace VlcNightlyUpdater.Services;

/// <summary>
/// Everything saved to %LOCALAPPDATA%\VlcNightlyUpdater\state.json. To add a setting: add a property here
/// (give it a default), add a row in Views/SettingsPanel.xaml + .cs. Unknown/old keys in the file are ignored.
/// </summary>
public sealed class AppState
{
    // ---- what is installed ----
    /// <summary>Folder name of the nightly we last installed/assumed, e.g. 20261021-0426.</summary>
    public string? InstalledBuild { get; set; }
    /// <summary>Git commit the installed build was made from (parsed from the installer's file name).</summary>
    public string? InstalledHash { get; set; }
    /// <summary>"x64" or "arm64".</summary>
    public string? InstalledArch { get; set; }
    /// <summary>The build before the current one (powers "what changed in my last update").</summary>
    public string? PreviousBuild { get; set; }
    public string? PreviousHash { get; set; }
    public DateTime? LastChecked { get; set; }

    // ---- settings (all shown in the Settings tab) ----
    /// <summary>true = install VLC updates as soon as the app opens; false = show "Update now" first.</summary>
    public bool AutoUpdate { get; set; } = true;
    public bool LaunchAfterUpdate { get; set; }
    /// <summary>Update this updater itself from GitHub Releases without asking.</summary>
    public bool AutoUpdateSelf { get; set; } = true;
    /// <summary>true = this updater also considers GitHub pre-releases and takes whichever version is newest.</summary>
    public bool AllowPreRelease { get; set; }
    /// <summary>OFF by default. true = a per-user scheduled task notifies about new nightlies.</summary>
    public bool BackgroundCheck { get; set; }
    /// <summary>"auto" (match the installed VLC), or "x64" / "arm64". Only shown on ARM64 PCs.</summary>
    public string ArchOverride { get; set; } = "auto";

    // ---- bookkeeping ----
    /// <summary>Newest build the background check already notified about (so it only tells you once).</summary>
    public string? LastNotifiedBuild { get; set; }
}
