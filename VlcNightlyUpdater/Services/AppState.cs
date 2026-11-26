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
    /// <summary>CPU type of <see cref="PreviousBuild"/> ("x64" / "arm64"). "Undo last update" only goes back to the same CPU type.</summary>
    public string? PreviousArch { get; set; }
    public DateTime? LastChecked { get; set; }

    // ---- settings (all shown in the Settings tab) ----
    /// <summary>true = install VLC updates as soon as the app opens; false = show "Update now" first.</summary>
    public bool AutoUpdate { get; set; } = true;
    public bool LaunchAfterUpdate { get; set; }
    /// <summary>true = if VLC is open during an update, wait until the user closes it (no question, VLC is never closed for them).</summary>
    public bool WaitForVlc { get; set; }
    /// <summary>How many verified installers to keep per CPU type for offline rollback / undo. Clamped on use (see <see cref="AppConfig.CacheKeepChoices"/>).</summary>
    public int CacheKeep { get; set; } = AppConfig.CacheKeep;
    /// <summary>Hours between automatic checks while the window stays open. Clamped on use (see <see cref="AppConfig.AutoCheckChoices"/>).</summary>
    public int AutoCheckHours { get; set; } = AppConfig.AutoCheckHours;
    /// <summary>true = the window stays above other windows.</summary>
    public bool AlwaysOnTop { get; set; }
    /// <summary>Update this updater itself from GitHub Releases without asking.</summary>
    public bool AutoUpdateSelf { get; set; } = true;
    /// <summary>true = this updater also considers GitHub pre-releases and takes whichever version is newest.</summary>
    public bool AllowPreRelease { get; set; }
    /// <summary>OFF by default. true = a per-user scheduled task notifies about new nightlies.</summary>
    public bool BackgroundCheck { get; set; }
    /// <summary>"auto" (match the installed VLC), or "x64" / "arm64". Only shown on ARM64 PCs.</summary>
    public string ArchOverride { get; set; } = "auto";
    /// <summary>Chosen accent color: "orange" (default), "system", "blue", "teal", "green", "purple", "pink", "red", "amber".</summary>
    public string AccentColor { get; set; } = "orange";

    /// <summary>true = show the full "What's new" dialog before updating this updater, and once after it was updated.</summary>
    public bool ShowWhatsNew { get; set; } = true;

    // ---- history & skipping ----
    /// <summary>A nightly the user chose to skip. Treated as "not newer" until a newer nightly shows up.</summary>
    public string? SkippedBuild { get; set; }
    /// <summary>Last installs, newest first (capped at <see cref="AppConfig.HistoryKeep"/>). Shown in the Builds tab.</summary>
    public List<HistoryEntry> History { get; set; } = new();

    // ---- bookkeeping ----
    /// <summary>The updater version that last ran. A different value on launch means "just updated": show What's new.</summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>Newest build the background check already notified about (so it only tells you once).</summary>
    public string? LastNotifiedBuild { get; set; }
    /// <summary>Newest updater version the background check already notified about.</summary>
    public string? LastNotifiedSelfVersion { get; set; }
}

/// <summary>One line of the install history. A plain class so System.Text.Json round-trips it without ceremony.</summary>
public sealed class HistoryEntry
{
    public DateTime When { get; set; }
    public string? From { get; set; }
    public string To { get; set; } = "";
    public string Arch { get; set; } = "";
}
