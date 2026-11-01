using System.Diagnostics;

namespace VlcNightlyUpdater.Services;

/// <summary>Everything about the running VLC program and its folders. No UI.</summary>
public static class VlcProcess
{
    public static bool IsRunning()
    {
        var procs = Process.GetProcessesByName("vlc");
        var running = procs.Length > 0;
        foreach (var p in procs) p.Dispose();
        return running;
    }

    public static void Launch()
    {
        var exe = VlcLocator.FindVlcExe();
        if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    /// <summary>Opens %APPDATA%\vlc (or %APPDATA% if VLC has no folder yet).</summary>
    public static void OpenConfigFolder()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vlc");
        if (!Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    /// <summary>Asks VLC to close like a user would (lets it save its settings).</summary>
    public static void CloseGently() => ForEach(p => p.CloseMainWindow());

    public static void Kill() => ForEach(p => p.Kill(entireProcessTree: true));

    /// <summary>Waits until VLC has exited or the polls run out. Returns true if it is gone.</summary>
    public static async Task<bool> WaitForExitAsync(int polls, int delayMs, CancellationToken ct)
    {
        for (var i = 0; i < polls && IsRunning(); i++) await Task.Delay(delayMs, ct);
        return !IsRunning();
    }

    private static void ForEach(Action<Process> act)
    {
        foreach (var p in Process.GetProcessesByName("vlc"))
        {
            try { act(p); } catch { }
            p.Dispose();
        }
    }
}
