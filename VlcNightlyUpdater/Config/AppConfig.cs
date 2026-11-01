using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater;

/// <summary>
/// EVERY URL, name and tunable number lives here. To change behaviour, change a value here, not inline.
/// (Static UI labels live in the .xaml next to the control; runtime messages live in Strings.cs;
/// colors live in Theme/Tokens.xaml; glyphs live in Icons.cs.)
/// </summary>
public static class AppConfig
{
    // ---- identity ----
    public const string AppName = "VLC Nightly Updater";
    public const string Repo = "StrikerXR/VLC-nightly-auto-updater";
    public const string RepoUrl = "https://github.com/" + Repo;

    // ---- window (in DIPs; shrunk automatically on small screens) ----
    public const int WindowWidth = 680;
    public const int WindowHeight = 800;
    public const int MinWindowsBuild = 17763; // Windows 10 1809

    // ---- VLC nightly channels (edit here if VideoLAN renames one) ----
    public const string NightlyRoot = "https://artifacts.videolan.org/vlc/";
    public static readonly IReadOnlyDictionary<VlcArch, string[]> Channels = new Dictionary<VlcArch, string[]>
    {
        [VlcArch.X64] = ["nightly-win64"],
        [VlcArch.Arm64] = ["nightly-win64-arm-llvm"],
    };
    public const int BuildsListed = 15;  // how many recent builds the "Builds" tab offers
    public const int LatestProbe = 5;    // newest folders tried when the very newest has no .exe yet

    // ---- "commits since" (public GitLab API, no login) ----
    public const string GitLabApi = "https://code.videolan.org/api/v4/projects/videolan%2Fvlc/repository";
    public const string GitLabWeb = "https://code.videolan.org/videolan/vlc";
    public const int ChangelogMaxShown = 150;

    // ---- updating this updater (GitHub Releases) ----
    public const string UpdaterAssetPrefix = "VlcNightlyUpdater-Setup-";
    public const string ChecksumFile = "SHA256SUMS.txt";
    public const int ReleasesToScan = 15; // used when pre-releases are allowed

    // ---- downloads and cache ----
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    public const int MaxDownloadAttempts = 6;
    public const int CacheKeep = 3;                       // installers kept per CPU type for offline rollback
    public const long MinFreeBytes = 400L * 1024 * 1024;  // refuse to download with less free disk than this

    // ---- optional background check. KEEP IN SYNC with installer.iss [Code] (it removes the same names) ----
    public const string TaskName = "VlcNightlyUpdater Background Check";
    public const string Aumid = "StrikerXR.VlcNightlyUpdater";
    public const string Protocol = "vlcnightlyupdater";
    public const int BackgroundEveryHours = 6;

    // ---- must equal AppId in installer.iss (Inno Setup appends "_is1") ----
    public const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B3F1C2A4-7D5E-4A9B-9C21-5E8F0A6D1B77}_is1";
}
