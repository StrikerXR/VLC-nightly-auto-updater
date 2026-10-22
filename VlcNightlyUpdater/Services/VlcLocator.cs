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
        // Both registry views, so a 32-bit updater still sees a 64-bit VLC (and the other way round).
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            var dir = ReadInstallDir(hive, view);
            if (!string.IsNullOrWhiteSpace(dir)) yield return dir;
        }

        // %ProgramW6432% is the real 64-bit Program Files even when this process is 32-bit.
        var pf64 = Environment.GetEnvironmentVariable("ProgramW6432")
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(pf64, "VideoLAN", "VLC");

        var pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrEmpty(pf86)) yield return Path.Combine(pf86, "VideoLAN", "VLC");
    }
}
