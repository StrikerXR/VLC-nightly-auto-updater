using Microsoft.Win32;

namespace VlcNightlyUpdater.Services;

public sealed record VlcInstall(string Exe, VlcArch? Arch);

public static class VlcLocator
{
    /// <summary>Finds an installed VLC. If <paramref name="prefer"/> is given and that architecture is installed, it wins.</summary>
    public static VlcInstall? Find(VlcArch? prefer = null)
    {
        VlcInstall? first = null;
        foreach (var dir in CandidateDirs())
        {
            var exe = Path.Combine(dir, "vlc.exe");
            if (!File.Exists(exe)) continue;

            var found = new VlcInstall(exe, ArchInfo.ReadPeArch(exe));
            if (prefer is null || found.Arch == prefer) return found;
            first ??= found;
        }
        return first;
    }

    public static string? FindVlcExe() => Find()?.Exe;

    private static string? ReadInstallDir(RegistryHive hive, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\VideoLAN\VLC");
            return key?.GetValue("InstallDir") as string;
        }
        catch { return null; }
    }

    private static IEnumerable<string> CandidateDirs()
    {
        // 64-bit and ARM64 VLC only (32-bit VLC is no longer supported).
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            var dir = ReadInstallDir(hive, RegistryView.Registry64);
            if (!string.IsNullOrWhiteSpace(dir)) yield return dir;
        }

        var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(pf64, "VideoLAN", "VLC");
    }
}
