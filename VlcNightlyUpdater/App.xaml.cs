using Microsoft.UI.Xaml;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater;

/// <summary>Startup only. Decides: background run (no window), a notification's Skip button (no window), already open (focus it), or show MainWindow.</summary>
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

        // A notification button: "vlcnightlyupdater:update" / ":skip?build=..." (see LaunchIntent.cs).
        var intent = LaunchIntent.Parse(Environment.GetCommandLineArgs());
        if (intent.Kind == IntentKind.Skip && intent.Build is not null && !SingleInstance.IsRunning())
        {
            // No window open: record the skip and leave quietly. (With a window open, it is simply brought forward below.)
            try { LaunchIntent.ApplySkip(intent.Build); } catch { }
            Environment.Exit(0);
            return;
        }
        LaunchIntent.Current = intent;

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
