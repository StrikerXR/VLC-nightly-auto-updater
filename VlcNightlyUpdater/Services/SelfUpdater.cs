using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace VlcNightlyUpdater.Services;

public sealed record SelfUpdateInfo(Version Version, string InstallerName, string InstallerUrl, string Sha256);

/// <summary>
/// Updates this app from its own GitHub Releases. Each release carries one setup per CPU type plus a
/// SHA256SUMS.txt; we pick the one that matches the running build, verify it, then hand over to the installer.
/// </summary>
public static class SelfUpdater
{
    public const string Repo = "StrikerXR/VLC-nightly-auto-updater";
    public const string RepoUrl = "https://github.com/" + Repo;

    // Must match AppId in installer.iss (with the "_is1" Inno Setup adds).
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B3F1C2A4-7D5E-4A9B-9C21-5E8F0A6D1B77}_is1";

    private static string TempDir => Path.Combine(Path.GetTempPath(), "VlcNightlyUpdater", "self");

    /// <summary>
    /// True only when this copy is the one the installer put on disk. A copy run from a build folder or
    /// debugger must never overwrite the installed app.
    /// </summary>
    public static bool IsInstalledCopy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            if (key?.GetValue("Inno Setup: App Path") is not string appPath) return false;

            static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');
            return string.Equals(Norm(appPath), Norm(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Returns the newer release for this CPU type, or null if we're current / nothing usable is published.</summary>
    public static async Task<SelfUpdateInfo?> CheckAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var resp = await Net.Http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null; // no releases yet
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var remote) || remote <= Net.CurrentVersion) return null;

        var arch = ArchInfo.ProcessArch.Key();
        string? exeName = null, exeUrl = null, sumsUrl = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString();
            if (name.StartsWith("VlcNightlyUpdater-Setup-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith($"-{arch}.exe", StringComparison.OrdinalIgnoreCase))
                (exeName, exeUrl) = (name, url);
            else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                sumsUrl = url;
        }
        if (exeName is null || exeUrl is null || sumsUrl is null) return null;

        // No checksum, no automatic install.
        var sums = await Net.Http.GetStringAsync(sumsUrl, ct).ConfigureAwait(false);
        var line = sums.Split('\n').FirstOrDefault(l => l.Contains(exeName));
        var sha = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(sha) ? null : new SelfUpdateInfo(remote, exeName, exeUrl, sha);
    }

    public static async Task<string> DownloadAsync(SelfUpdateInfo info, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        var path = Path.Combine(TempDir, info.InstallerName);
        await Downloader.DownloadAsync(info.InstallerUrl, path, progress, ct).ConfigureAwait(false);

        await using var fs = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
        await fs.DisposeAsync().ConfigureAwait(false);

        if (!actual.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
            throw new InvalidDataException("The updater download failed its SHA256 check, so it was not installed.");
        }
        return path;
    }

    /// <summary>
    /// Starts the new setup silently (it shows only a small progress window) and asks it to reopen the app when
    /// done. The caller must exit right after, so the installer can replace the program files.
    /// </summary>
    public static void LaunchInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path, "/SILENT /SP- /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /relaunch=1")
        {
            UseShellExecute = true
        });

    /// <summary>Delete setup files left over from earlier self-updates.</summary>
    public static void CleanupStale()
    {
        try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, recursive: true); } catch { }
    }
}
