using System.Security.Cryptography;

namespace VlcNightlyUpdater.Services;

/// <summary>
/// Keeps the last few verified installers in %LOCALAPPDATA%\VlcNightlyUpdater\cache\&lt;arch&gt;, so a rollback or
/// reinstall doesn't need the network. Each installer has a ".sha512" sidecar written when it was verified;
/// a cached file is only trusted if it still hashes to that value.
/// </summary>
public static class InstallerCache
{
    public const int Keep = 3;
    private const long MinFreeBytes = 400L * 1024 * 1024;

    public static string Root => Path.Combine(StateStore.Dir, "cache");
    public static string DirFor(VlcArch arch) => Path.Combine(Root, arch.Key());

    /// <summary>"{folder}_{exe}" so we can tell which nightly a cached file belongs to.</summary>
    public static string PathFor(BuildInfo b) => Path.Combine(DirFor(b.Arch), $"{b.Folder}_{b.ExeName}");

    private static string Sidecar(string exe) => exe + ".sha512";

    public static bool Has(string folder, VlcArch arch)
    {
        try
        {
            var dir = DirFor(arch);
            return Directory.Exists(dir) &&
                   Directory.EnumerateFiles(dir, $"{folder}_*.exe").Any(f => File.Exists(Sidecar(f)));
        }
        catch { return false; }
    }

    /// <summary>Rebuilds a BuildInfo from a cached file (works offline).</summary>
    public static BuildInfo? TryGetBuild(string folder, VlcArch arch)
    {
        try
        {
            var dir = DirFor(arch);
            if (!Directory.Exists(dir)) return null;
            var file = Directory.EnumerateFiles(dir, $"{folder}_*.exe").FirstOrDefault(f => File.Exists(Sidecar(f)));
            if (file is null) return null;

            var exeName = Path.GetFileName(file)[(folder.Length + 1)..];
            return new BuildInfo(folder, exeName, "", "", UpdateService.HashFromExeName(exeName), arch);
        }
        catch { return null; }
    }

    public static async Task<bool> IsValidAsync(string exe, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(exe) || !File.Exists(Sidecar(exe))) return false;
            var expected = (await File.ReadAllTextAsync(Sidecar(exe), ct).ConfigureAwait(false)).Trim();
            await using var fs = File.OpenRead(exe);
            var actual = await Hashing.Sha512HexAsync(fs, ct).ConfigureAwait(false);
            return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    public static void MarkVerified(string exe, string sha512Hex)
    {
        try { File.WriteAllText(Sidecar(exe), sha512Hex); } catch { /* cache is best-effort */ }
    }

    public static void Delete(string exe)
    {
        foreach (var f in new[] { exe, Sidecar(exe), exe + ".part" })
            try { File.Delete(f); } catch { }
    }

    public static void Touch(string exe)
    {
        try { File.SetLastWriteTimeUtc(exe, DateTime.UtcNow); } catch { }
    }

    /// <summary>Keep the newest few installers per architecture; drop the rest plus abandoned partial downloads.</summary>
    public static void Prune(VlcArch arch, int keep = Keep, string? protect = null)
    {
        try
        {
            var dir = DirFor(arch);
            if (!Directory.Exists(dir)) return;

            var exes = new DirectoryInfo(dir).GetFiles("*.exe").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            foreach (var old in exes.Skip(keep))
                if (!string.Equals(old.FullName, protect, StringComparison.OrdinalIgnoreCase))
                    Delete(old.FullName);

            foreach (var part in new DirectoryInfo(dir).GetFiles("*.part"))
                if (DateTime.UtcNow - part.LastWriteTimeUtc > TimeSpan.FromDays(3))
                    try { part.Delete(); } catch { }
        }
        catch { /* best-effort */ }
    }

    public static (int Count, long Bytes) Stats()
    {
        try
        {
            if (!Directory.Exists(Root)) return (0, 0);
            var files = new DirectoryInfo(Root).GetFiles("*.exe", SearchOption.AllDirectories);
            return (files.Length, files.Sum(f => f.Length));
        }
        catch { return (0, 0); }
    }

    public static void Clear()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); } catch { }
    }

    /// <summary>Fail early with a clear message instead of a half-written download.</summary>
    public static void EnsureSpace(VlcArch arch)
    {
        try
        {
            var dir = DirFor(arch);
            Directory.CreateDirectory(dir);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!);
            if (drive.AvailableFreeSpace < MinFreeBytes)
                throw new InvalidOperationException(
                    $"Not enough free space on {drive.Name} (need about {MinFreeBytes / 1024 / 1024} MB, have {drive.AvailableFreeSpace / 1024 / 1024} MB).");
        }
        catch (InvalidOperationException) { throw; }
        catch { /* can't tell -> don't block */ }
    }
}
