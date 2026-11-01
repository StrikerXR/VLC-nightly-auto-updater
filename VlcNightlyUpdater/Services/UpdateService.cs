using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VlcNightlyUpdater.Services;

public sealed record BuildInfo(string Folder, string ExeName, string ExeUrl, string ShaUrl, string? Hash, VlcArch Arch);

public enum InstallStage { UsingCache, Downloading, Verifying }

public sealed class UpdateService
{
    // Channel names live in AppConfig.Channels. Candidates are tried in order; the first that lists builds wins.
    private const string Root = AppConfig.NightlyRoot;
    private static IReadOnlyDictionary<VlcArch, string[]> Channels => AppConfig.Channels;

    private string? _baseUrl;
    private VlcArch _baseArch;

    /// <summary>Which architecture's channel to talk to. Set before listing.</summary>
    public VlcArch Arch { get; set; } = ArchInfo.Native;

    private string BaseUrl =>
        _baseUrl is not null && _baseArch == Arch ? _baseUrl : $"{Root}{Channels[Arch][0]}/";

    /// <summary>"…-win64-1a2b3c4d.exe" -> "1a2b3c4d" (the git commit the build was made from).</summary>
    public static string? HashFromExeName(string exeName)
    {
        var m = Regex.Match(exeName, @"-([0-9a-f]{7,40})\.exe$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>All dated build folders, newest first (e.g. 20261021-0426).</summary>
    public async Task<List<string>> ListFoldersAsync(CancellationToken ct)
    {
        Exception? last = null;
        foreach (var channel in Channels[Arch])
        {
            var url = $"{Root}{channel}/";
            try
            {
                var html = await Net.Http.GetStringAsync(url, ct).ConfigureAwait(false);
                var folders = Regex.Matches(html, @"href=""(\d{8}-\d{4})/""")
                    .Select(m => m.Groups[1].Value)
                    .Distinct()
                    .OrderByDescending(f => f, StringComparer.Ordinal)
                    .ToList();
                if (folders.Count == 0) continue;

                _baseUrl = url;
                _baseArch = Arch;
                return folders;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is not null)
            {
                last = ex; // this channel doesn't exist (404 etc.): try the next candidate
            }
            // no status code = network problem, which would hit every channel too: let it surface as-is
        }
        throw new InvalidOperationException(
            $"Couldn't find a {Arch.Label()} nightly channel on artifacts.videolan.org.", last);
    }

    /// <summary>Finds the .exe installer inside one build folder, or null if it isn't there (yet).</summary>
    public async Task<BuildInfo?> ResolveAsync(string folder, CancellationToken ct)
    {
        var folderUrl = $"{BaseUrl}{folder}/";
        string page;
        try { page = await Net.Http.GetStringAsync(folderUrl, ct).ConfigureAwait(false); }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; } // HTTP timeout

        var exe = Regex.Matches(page, @"href=""(vlc-[^""/]+\.exe)""")
            .Select(m => m.Groups[1].Value)
            .FirstOrDefault(n => !n.Contains("debug", StringComparison.OrdinalIgnoreCase));
        if (exe is null) return null;

        return new BuildInfo(folder, exe, folderUrl + exe, folderUrl + "SHA512SUM", HashFromExeName(exe), Arch);
    }

    /// <summary>Newest build that actually has an .exe (the very newest may still be uploading).</summary>
    public async Task<BuildInfo> GetLatestAsync(IEnumerable<string> folders, CancellationToken ct)
    {
        foreach (var folder in folders.Take(AppConfig.LatestProbe))
        {
            var b = await ResolveAsync(folder, ct).ConfigureAwait(false);
            if (b is not null) return b;
        }
        throw new InvalidOperationException($"Couldn't find a {Arch.Label()} nightly build with an .exe installer.");
    }

    /// <summary>
    /// Returns a verified installer on disk: from the cache if it's still intact, otherwise downloaded (resuming
    /// any partial file) and checked against VideoLAN's SHA512SUM. A corrupt download is retried once from scratch.
    /// </summary>
    public async Task<string> EnsureInstallerAsync(
        BuildInfo build, IProgress<DownloadProgress> progress, IProgress<InstallStage> stage, CancellationToken ct)
    {
        var path = InstallerCache.PathFor(build);

        if (File.Exists(path))
        {
            stage.Report(InstallStage.UsingCache);
            if (await InstallerCache.IsValidAsync(path, ct).ConfigureAwait(false))
            {
                InstallerCache.Touch(path);
                return path;
            }
            InstallerCache.Delete(path);
        }

        if (string.IsNullOrEmpty(build.ExeUrl))
            throw new InvalidOperationException("That build isn't cached and there's no download link for it.");

        InstallerCache.EnsureSpace(build.Arch);

        for (var attempt = 1; ; attempt++)
        {
            stage.Report(InstallStage.Downloading);
            await Downloader.DownloadAsync(build.ExeUrl, path, progress, ct).ConfigureAwait(false);

            stage.Report(InstallStage.Verifying);
            try
            {
                await VerifyAsync(path, build, ct).ConfigureAwait(false);
                return path;
            }
            catch (InvalidDataException) when (attempt == 1)
            {
                // Corrupt (a resumed .part can carry bad bytes). VerifyAsync already deleted it: go again from zero.
            }
        }
    }

    private static async Task VerifyAsync(string file, BuildInfo build, CancellationToken ct)
    {
        var sums = await Net.Http.GetStringAsync(build.ShaUrl, ct).ConfigureAwait(false);
        var line = sums.Split('\n').FirstOrDefault(l => l.Contains(build.ExeName))
                   ?? throw new InvalidOperationException("SHA512SUM has no entry for the installer.");
        var expected = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        await using var fs = File.OpenRead(file);
        var actual = Convert.ToHexString(await SHA512.HashDataAsync(fs, ct).ConfigureAwait(false));
        await fs.DisposeAsync().ConfigureAwait(false);

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            InstallerCache.Delete(file);
            throw new InvalidDataException("SHA512 mismatch, the download is corrupt. Not installing.");
        }
        InstallerCache.MarkVerified(file, actual);
    }

    /// <summary>NSIS silent install over the existing install. Never uninstalls, so %APPDATA%\vlc is untouched.</summary>
    public static async Task<int> InstallAsync(string installerPath)
    {
        var psi = new ProcessStartInfo(installerPath, "/S")
        {
            UseShellExecute = true,
            Verb = "runas" // one UAC prompt; the app itself stays unelevated
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start the installer.");
        await p.WaitForExitAsync().ConfigureAwait(false);
        return p.ExitCode;
    }
}
