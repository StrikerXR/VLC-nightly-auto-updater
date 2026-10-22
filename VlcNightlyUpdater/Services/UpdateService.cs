using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VlcNightlyUpdater.Services;

public sealed record BuildInfo(string Folder, string ExeName, string ExeUrl, string ShaUrl);

public sealed class UpdateService
{
    private const string BaseUrl = "https://artifacts.videolan.org/vlc/nightly-win64/";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("VlcNightlyUpdater/1.0");
        return c;
    }

    /// <summary>All dated build folders, newest first (e.g. 20261021-0426).</summary>
    public async Task<List<string>> ListFoldersAsync(CancellationToken ct)
    {
        var html = await Http.GetStringAsync(BaseUrl, ct);
        return Regex.Matches(html, @"href=""(\d{8}-\d{4})/""")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Finds the win64 .exe installer inside one build folder, or null if it isn't there (yet).</summary>
    public async Task<BuildInfo?> ResolveAsync(string folder, CancellationToken ct)
    {
        var folderUrl = $"{BaseUrl}{folder}/";
        string page;
        try { page = await Http.GetStringAsync(folderUrl, ct); }
        catch (HttpRequestException) { return null; }

        var m = Regex.Match(page, @"href=""(vlc-[^""/]+-win64-[0-9a-f]+\.exe)""");
        if (!m.Success) return null;

        var exe = m.Groups[1].Value;
        return new BuildInfo(folder, exe, folderUrl + exe, folderUrl + "SHA512SUM");
    }

    /// <summary>Newest build that actually has an .exe (the very newest may still be uploading).</summary>
    public async Task<BuildInfo> GetLatestAsync(IEnumerable<string> folders, CancellationToken ct)
    {
        foreach (var folder in folders.Take(5))
        {
            var b = await ResolveAsync(folder, ct);
            if (b is not null) return b;
        }
        throw new InvalidOperationException("Couldn't find a nightly build with an .exe installer.");
    }

    public async Task<string> DownloadAsync(BuildInfo build, IProgress<double> progress, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "VlcNightlyUpdater");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, build.ExeName);

        using var resp = await Http.GetAsync(build.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);
        var buf = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress.Report(read * 100.0 / total);
        }
        return path;
    }

    public async Task VerifyAsync(string file, BuildInfo build, CancellationToken ct)
    {
        var sums = await Http.GetStringAsync(build.ShaUrl, ct);
        var line = sums.Split('\n').FirstOrDefault(l => l.Contains(build.ExeName))
                   ?? throw new InvalidOperationException("SHA512SUM has no entry for the installer.");
        var expected = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        await using var fs = File.OpenRead(file);
        var actual = Convert.ToHexString(await SHA512.HashDataAsync(fs, ct));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SHA512 mismatch, download is corrupt. Not installing.");
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
        await p.WaitForExitAsync();
        return p.ExitCode;
    }
}
