using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater;

/// <summary>
/// Every message the app writes at RUNTIME (status lines, dialogs, notes). Reword anything here without touching logic.
/// Static labels (button text, tab names, setting titles) are written directly in the .xaml files.
/// </summary>
public static class Strings
{
    // ---- generic ----
    public const string Cancel = "Cancel";
    public const string Ok = "OK";

    // ---- check flow ----
    public const string OsTooOld = "Windows 10 (version 1809) or newer required";
    public const string OsTooOldDetail = "This updater doesn't support older Windows. Update Windows and try again.";
    public const string Checking = "Checking for updates\u2026";
    public const string CheckingDetail = "Contacting GitHub and artifacts.videolan.org";
    public const string CheckingVlc = "Checking for VLC updates\u2026";
    public static string ChannelDetail(VlcArch a) => $"{a.Label()} nightly channel";
    public const string FirstRun = "You're all set";
    public const string FirstRunDetail = "First run: assuming your VLC is already on the latest nightly.";
    public const string UpToDate = "VLC is up to date";
    public static string UpToDateDetail(VlcArch a) => $"You're on the newest {a.Label()} nightly.";
    public const string UpdateAvailable = "Update available";
    public static string UpdateDetail(string? from, string to) => $"{from ?? "Not installed"}  \u2192  {to}";
    public const string SwitchBuild = "Switch VLC build?";
    public static string SwitchDetail(VlcArch from, VlcArch to, string folder) => $"{from.Label()}  \u2192  {to.Label()}  ({folder})";

    // ---- progress ----
    public const string Reconnecting = "Reconnecting\u2026";
    public const string Preparing = "Preparing\u2026";
    public const string CheckingCache = "Checking the cached installer\u2026";
    public const string Downloading = "Downloading";
    public const string Verifying = "Verifying download\u2026";
    public const string VerifyingDetail = "Checking SHA512";
    public const string ClosingVlc = "Closing VLC\u2026";
    public const string ClosingVlcDetail = "Waiting for it to shut down cleanly.";
    public const string Installing = "Installing\u2026";
    public const string InstallingDetail = "Approve the UAC prompt to continue.";
    public const string Retrying = "Retrying the install\u2026";
    public static string RetryDetail(int code) => $"The installer exited with code {code}. Trying once more.";
    public static string InstallFailed(int code) => $"The VLC installer exited with code {code}.";

    // ---- results ----
    public const string Updated = "Updated successfully";
    public static string UpdatedDetail(string folder, VlcArch a, string note) =>
        $"VLC is now on {folder} ({a.Label()}). Your settings were kept.{note}";
    public static string ArchNote(VlcArch actual, VlcArch wanted) =>
        $" (Heads up: the installed vlc.exe is {actual.Label()}, not {wanted.Label()}.)";
    public const string NoVlcAfter = "The installer finished, but vlc.exe wasn't found afterwards.";
    public const string NoExeForBuild = "That build has no .exe installer, and nothing is cached for it.";
    public const string CancelledTitle = "Cancelled";
    public const string CancelledDetail = "Nothing was installed. A partly downloaded file is kept and will resume next time.";
    public const string LeftAsIs = "VLC was left as it is.";
    public const string UpdateCancelled = "Update cancelled";
    public const string VlcStillRunning = "VLC is still running. Close it and press Update now.";
    public const string UacDeclined = "The UAC prompt was declined.";
    public const string Timeout = "The server took too long";
    public const string TimeoutDetail = "Check your connection and press Check again.";
    public const string WentWrong = "Something went wrong";

    // ---- VLC running dialogs ----
    public const string VlcRunningTitle = "VLC is running";
    public const string VlcRunningBody = "VLC has to close before it can be updated. I'll close it gently and reopen it when the update is done.";
    public const string CloseAndUpdate = "Close VLC and update";
    public const string VlcStuckTitle = "VLC isn't closing";
    public const string VlcStuckBody = "It may be busy or showing a prompt. Force it to close? Anything unsaved in VLC will be lost.";
    public const string ForceClose = "Force close";
    public static string SwitchDialogTitle(VlcArch to) => $"Switch VLC to the {to.Label()} build?";
    public static string SwitchDialogBody(VlcArch from, VlcArch to) =>
        $"VLC on this PC is {from.Label()}. This installs the {to.Label()} nightly instead. " +
        "Your VLC settings are kept, but VLC's own installer decides what happens to the old install.";
    public static string InstallArch(VlcArch to) => $"Install {to.Label()}";

    // ---- updating this updater ----
    public const string SelfUpdating = "Updating the updater\u2026";
    public static string SelfUpdateRange(Version to) => $"v{Net.CurrentVersion}  \u2192  v{to}";
    public const string Restarting = "Restarting\u2026";
    public static string RestartingDetail(Version v) => $"Installing v{v}. This window reopens by itself.";
    public static string SelfAvailable(SelfUpdateInfo i) =>
        $"Version {i.Version}{(i.PreRelease ? " (pre-release)" : "")} is available (you have {Net.CurrentVersion}).";
    public static string SelfFailed(string why) => $"Automatic update failed: {why}";

    // ---- changes panel / tiles ----
    public const string ChangesNone = "Nothing to compare yet. When a newer nightly is out, the commits since your build show up here.";
    public const string ChangesLoading = "Loading commits\u2026";
    public const string ChangesApprox = "Matched by build time, so it may include a few extra commits or miss one at the edges.";
    public const string ChangesEmpty = "No commits found for this range.";
    public static string ChangesFailed(string why) => $"Couldn't load the commits: {why}";
    public static string CommitCount(int n) => $"{n} commit{(n == 1 ? "" : "s")}";
    public static string ChangesCapped(int shown, int total) => $"Showing the newest {shown} of {total}. Open GitLab for the full list.";
    public const string WhatsNew = "Commits since your build";
    public const string LastUpdate = "What changed in your last update";
    public const string BehindCaption = "commits behind";
    public const string BehindNone = "you're current";

    // ---- tabs: settings / builds ----
    public const string BackgroundOn = "On: a scheduled task checks at sign-in and every 6 hours and shows a notification. It never installs by itself.";
    public const string BackgroundOff = "Off: nothing runs in the background.";
    public const string BackgroundInstalledOnly = "Only available in the installed copy of the updater.";
    public const string BackgroundOnFailed = "Couldn't turn on background checks";
    public const string BackgroundOffFailed = "Couldn't turn off background checks";
    public const string PickBuild = "Pick a build\u2026";
    public const string CachedMark = "cached";
    public const string NoCache = "No cached installers";
    public static string CacheInfo(int n, long bytes) =>
        $"{n} cached installer{(n == 1 ? "" : "s")}{Format.Dot}{Format.Bytes(bytes)} (for offline rollback)";
    public static string LastChecked(DateTime t) => $"Last checked {t:t}";
}
