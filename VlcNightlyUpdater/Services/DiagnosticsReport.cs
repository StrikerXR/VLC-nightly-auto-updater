using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VlcNightlyUpdater.Services;

/// <summary>
/// Builds the plain-text "Copy diagnostics" report for bug reports. No UI, no network, nothing secret: versions, what
/// is installed, the settings and the last status line. Paths under the user's profile are shortened. To add a line,
/// add it in <see cref="Build"/>; keep it short and keep personal data out.
/// </summary>
public static class DiagnosticsReport
{
    public static string Build(AppState s, VlcArch target, string? latestFolder, string statusTitle, string? statusDetail, string phase)
    {
        static string OnOff(bool b) => b ? "on" : "off";
        var sb = new StringBuilder();
        void Line(string label, string? value) => sb.AppendLine($"{label,-14}{(string.IsNullOrWhiteSpace(value) ? "-" : value)}");

        sb.AppendLine($"{AppConfig.AppName} diagnostics");
        sb.AppendLine("Made by the updater. Your user folder is shortened; look it over before you share it.");
        sb.AppendLine();

        var mode = Portable.IsPortable ? "portable" : SelfUpdater.IsInstalledCopy() ? "installed" : "run from a build folder";
        Line("Updater", $"{Net.CurrentVersion} · {mode} · {ArchInfo.ProcessArch.Label()} app");
        Line("Windows", $"{Environment.OSVersion.Version} · {RuntimeInformation.OSArchitecture}");

        var vlc = VlcLocator.Find();
        Line("VLC", vlc is null ? "not found" : Format.Join(vlc.Arch?.Label() ?? "unknown CPU type", VlcVersion(vlc.Exe)).Replace("  ·  ", " · "));
        Line("Channel", $"{target.Label()} nightly");
        Line("Installed", Describe(s.InstalledBuild, s.InstalledHash, s.InstalledArch));
        Line("Previous", Describe(s.PreviousBuild, s.PreviousHash, s.PreviousArch));
        Line("Newest seen", latestFolder);
        Line("Skipped", s.SkippedBuild);
        Line("Last checked", s.LastChecked?.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine();

        Line("Status", $"{phase} · {statusTitle}");
        if (!string.IsNullOrWhiteSpace(statusDetail)) Line("", statusDetail);
        sb.AppendLine();

        sb.AppendLine("Settings");
        Line("  Auto-update", OnOff(s.AutoUpdate));
        Line("  Open VLC after", OnOff(s.LaunchAfterUpdate));
        Line("  Wait for VLC", OnOff(s.WaitForVlc));
        Line("  Self-update", OnOff(s.AutoUpdateSelf));
        Line("  What's new", OnOff(s.ShowWhatsNew));
        Line("  Pre-releases", OnOff(s.AllowPreRelease));
        Line("  Background", OnOff(s.BackgroundCheck));
        Line("  CPU override", s.ArchOverride);
        Line("  Cache keeps", s.CacheKeep.ToString());

        var (count, bytes) = InstallerCache.Stats();
        Line("Cache", $"{count} installer{(count == 1 ? "" : "s")} · {Format.Bytes(bytes)}");

        return Shorten(sb.ToString().TrimEnd());
    }

    private static string Describe(string? build, string? hash, string? arch) =>
        build is null ? "" : Format.Join(build, hash, ArchInfo.ParseKey(arch)?.Label()).Replace("  ·  ", " · ");

    private static string? VlcVersion(string exe)
    {
        try { return FileVersionInfo.GetVersionInfo(exe).ProductVersion; }
        catch { return null; }
    }

    /// <summary>Replaces the profile folder (and a user name of 3+ letters) so error messages don't leak it.</summary>
    private static string Shorten(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile)) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        if (Environment.UserName.Length >= 3) text = text.Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
