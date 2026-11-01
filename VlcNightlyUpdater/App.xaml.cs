using Microsoft.UI.Xaml;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater;

/// <summary>Startup only. Decides: background run (no window), already open (focus it), or show MainWindow.</summary>
public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

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

        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.FocusExisting();
            Environment.Exit(0);
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }
}
