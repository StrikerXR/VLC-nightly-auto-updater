using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater;

public partial class App : Application
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    private const int SW_RESTORE = 9;

    private Window? _window;
    private static Mutex? _single; // held for the life of the process, released by Windows when we exit

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // The optional scheduled task starts us with --background-check: no window, check, maybe notify, exit.
        if (BackgroundCheck.IsBackgroundRun(Environment.GetCommandLineArgs()))
        {
            try { await BackgroundCheck.RunAsync(); }
            catch { /* offline, rate-limited, ...: try again next time */ }
            finally { Environment.Exit(0); }
            return;
        }

        // Only one window at a time (clicking the notification while the app is open should just show it).
        // Wait a moment so a self-update relaunch isn't blocked by the old process that is still closing.
        _single = new Mutex(false, @"Local\VlcNightlyUpdater.SingleInstance");
        bool owned;
        try { owned = _single.WaitOne(TimeSpan.FromSeconds(3)); }
        catch (AbandonedMutexException) { owned = true; }

        if (!owned)
        {
            var hwnd = FindWindow(null, "VLC Nightly Updater");
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
            }
            Environment.Exit(0);
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }
}
