using System.Runtime.InteropServices;

namespace VlcNightlyUpdater.Services;

/// <summary>Only one updater window at a time. A second launch just brings the first one to the front.</summary>
public static class SingleInstance
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    private const int SW_RESTORE = 9;
    private static Mutex? _mutex; // held for the life of the process; Windows releases it when we exit

    /// <summary>Waits a moment so a self-update relaunch isn't blocked by the old process that is still closing.</summary>
    public static bool TryAcquire()
    {
        _mutex = new Mutex(false, @"Local\VlcNightlyUpdater.SingleInstance");
        try { return _mutex.WaitOne(TimeSpan.FromSeconds(3)); }
        catch (AbandonedMutexException) { return true; }
    }

    /// <summary>Finds the already-open window (by its title, set from AppConfig.AppName) and shows it.</summary>
    public static void FocusExisting()
    {
        var hwnd = FindWindow(null, AppConfig.AppName);
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }
}
