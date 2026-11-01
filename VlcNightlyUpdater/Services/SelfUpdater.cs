using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace VlcNightlyUpdater.Services;

public sealed record SelfUpdateInfo(Version Version, string InstallerName, string InstallerUrl, string Sha256, bool PreRelease);

/// <summary>
/// Updates this app from its own GitHub Releases. Each release carries one setup per CPU type plus a
/// SHA256SUMS.txt; we pick the one that matches the running build, verify it, then hand over to the installer.
///
/// Normal mode asks GitHub for "latest" (stable releases only).
/// Pre-release mode (AppState.AllowPreRelease) lists recent releases and takes the HIGHEST VERSION, whether or
/// not GitHub flags it as a pre-release. Drafts are always ignored.
/// </summary>
public static class SelfUpdater
{
    private const string Api = "https://api.github.com/repos/" + AppConfig.Repo + "/releases";

    private static string TempDir => Path.Combine(Path.GetTempPath(), "VlcNightlyUpdater", "self");

    /// <summary>
    /// True only when this copy is the one the installer put on disk. A copy run from a build folder or
    /// debugger must never overwrite the installed app.
    /// </summary>
    public static bool IsInstalledCopy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppConfig.UninstallKey);
            if (key?.GetValue("Inno Setup: App Path") is not string appPath) return false;

            static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');
            return string.Equals(Norm(appPath), Norm(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Returns the newer release for this CPU type, or null if we're current / nothing usable is published.</summary>
    public static async Task<SelfUpdateInfo?> CheckAsync(bool allowPreRelease, CancellationToken ct)
    {
        var url = allowPreRelease ? $"{Api}?per_page={AppConfig.ReleasesToScan}" : $"{Api}/latest";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var resp = await Net.Http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null; // no releases yet
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var releases = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : new List<JsonElement> { doc.RootElement };

        // Newest version first. The first one that is newer than us AND has a usable setup wins.
        var ranked = releases
            .Where(r => !Flag(r, "draft"))
            .Select(r => (Release: r, Version: ParseTag(r)))
            .Where(x => x.Version is not null)
            .OrderByDescending(x => x.Version)
            .ToList();

        foreach (var (release, version) in ranked)
        {
            if (version! <= Net.CurrentVersion) return null; // nothing newer than what's running
            var info = await ReadReleaseAsync(release, version!, ct).ConfigureAwait(false);
            if (info is not null) return info;               // else try the next newest (e.g. ARM64 setup missing)
        }
        return null;
    }

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>"v3.1.0" or "v3.1.0-beta.2" -> 3.1.0</summary>
    private static Version? ParseTag(JsonElement release)
    {
        var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        tag = tag.TrimStart('v', 'V');
        var cut = tag.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) tag = tag[..cut];
        return Version.TryParse(tag, out var v) ? v : null;
    }

    private static async Task<SelfUpdateInfo?> ReadReleaseAsync(JsonElement release, Version version, CancellationToken ct)
    {
        var arch = ArchInfo.ProcessArch.Key();
        string? exeName = null, exeUrl = null, sumsUrl = null;
        foreach (var a in release.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString();
            if (name.StartsWith(AppConfig.UpdaterAssetPrefix, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith($"-{arch}.exe", StringComparison.OrdinalIgnoreCase))
                (exeName, exeUrl) = (name, url);
            else if (name.Equals(AppConfig.ChecksumFile, StringComparison.OrdinalIgnoreCase))
                sumsUrl = url;
        }
        if (exeName is null || exeUrl is null || sumsUrl is null) return null;

        // No checksum, no automatic install.
        var sums = await Net.Http.GetStringAsync(sumsUrl, ct).ConfigureAwait(false);
        var line = sums.Split('\n').FirstOrDefault(l => l.Contains(exeName));
        var sha = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(sha)
            ? null
            : new SelfUpdateInfo(version, exeName, exeUrl, sha, Flag(release, "prerelease"));
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
