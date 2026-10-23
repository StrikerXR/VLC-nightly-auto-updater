using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VlcNightlyUpdater.Services;

public sealed record StableBuild(string Version, string ExeName, string ExeUrl, string ShaUrl);

/// <summary>Finds, downloads and verifies the latest STABLE VLC installer from get.videolan.org.</summary>
public static class StableService
{
    private const string Root = "https://get.videolan.org/vlc/last/";

    // ARM64 users get the native stable build if VideoLAN publishes one, otherwise the 64-bit one (runs emulated).
    private static readonly Dictionary<VlcArch, string[]> Dirs = new()
    {
        [VlcArch.X64] = ["win64"],
        [VlcArch.Arm64] = ["winarm64", "win64"],
    };

    private static string TempDir => Path.Combine(Path.GetTempPath(), "VlcNightlyUpdater", "stable");

    public static async Task<StableBuild> ResolveAsync(VlcArch arch, CancellationToken ct)
    {
        foreach (var dir in Dirs[arch])
        {
            try
            {
                using var resp = await Net.Http.GetAsync($"{Root}{dir}/", ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var baseUri = resp.RequestMessage?.RequestUri ?? new Uri($"{Root}{dir}/");

                var m = Regex.Match(html, @"href=""(vlc-(\d+(?:\.\d+)+)-win(?:64|arm64)\.exe)""", RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                var exe = m.Groups[1].Value;
                return new StableBuild(m.Groups[2].Value, exe, new Uri(baseUri, exe).ToString(), new Uri(baseUri, exe + ".sha256").ToString());
            }
            catch (HttpRequestException) { /* try the next candidate */ }
        }
        throw new InvalidOperationException("Couldn't find a stable VLC installer on get.videolan.org.");
    }

    /// <summary>Downloads (resuming a partial file) and checks SHA-256. Returns the installer path.</summary>
    public static async Task<string> DownloadAsync(StableBuild b, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, b.ExeName);

        var sums = await Net.Http.GetStringAsync(b.ShaUrl, ct).ConfigureAwait(false);
        var expected = sums.Split([' ', '\t', '*', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                       ?? throw new InvalidOperationException("VideoLAN's checksum file was empty.");

        await Downloader.DownloadAsync(b.ExeUrl, path, progress, ct).ConfigureAwait(false);

        await using var fs = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
        await fs.DisposeAsync().ConfigureAwait(false);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
            throw new InvalidDataException("SHA-256 mismatch, the download is corrupt. Not installing.");
        }
        return path;
    }
}
