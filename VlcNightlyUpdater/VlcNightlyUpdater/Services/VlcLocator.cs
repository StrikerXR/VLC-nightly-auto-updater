using Microsoft.Win32;

namespace VlcNightlyUpdater.Services;

public static class VlcLocator
{
    public static string? FindVlcExe()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\VideoLAN\VLC");
            if (key?.GetValue("InstallDir") is string dir)
            {
                var exe = Path.Combine(dir, "vlc.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        catch { /* fall through */ }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe");
        return File.Exists(fallback) ? fallback : null;
    }
}
