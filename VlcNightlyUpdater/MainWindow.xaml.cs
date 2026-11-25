using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VlcNightlyUpdater.Services;
using VlcNightlyUpdater.Views;

namespace VlcNightlyUpdater;

/// <summary>
/// The window's state and wiring. The actual work is split by topic, one file each:
///   MainWindow.Chrome.cs      title bar, backdrop, icon, size
///   MainWindow.Check.cs       "check for updates" flow
///   MainWindow.Install.cs     download + install flow, progress
///   MainWindow.SelfUpdate.cs  updating this updater
///   MainWindow.Vlc.cs         closing/reopening VLC, dialogs
///   MainWindow.Changes.cs     the "commits since" tab and behind-count tile
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly UpdateService _updates = new();
    private readonly CancellationTokenSource _cts = new();   // the window: cancelled when it closes
    private CancellationTokenSource? _opCts;                  // one action: cancelled by the Cancel button too
    private readonly AppState _state = StateStore.Load();

    private readonly DispatcherTimer _autoCheckTimer = new();
    private DispatcherTimer? _retryTimer;

    private BuildInfo? _pending;        // an update waiting for the user to press "Update now"
    private BuildInfo? _latest;         // newest build found by the last check
    private SelfUpdateInfo? _selfUpdate;
    private VlcArch _target = ArchInfo.Native;
    private bool _pendingIsSwitch;  // the waiting update changes CPU type (can't be skipped)
    private bool _busy, _started, _closed;

    private CancellationToken Ct => _opCts?.Token ?? _cts.Token;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppConfig.AppName;
        SetupWindow();
        ApplyAccent();

        VersionText.Text = $"v{Net.CurrentVersion}" + (Portable.IsPortable ? Format.Dot + "portable" : "") + (IsDecember ? " \U0001F384" : "");

        Settings.Load(_state);
        SelfUpdater.CleanupStale();
        Builds.Refresh();
        Builds.SetHistory(_state.History);
        Tiles.SetInstalled(_state.InstalledBuild, ArchInfo.ParseKey(_state.InstalledArch));
        Actions.SetLastChecked(_state.LastChecked);

        // wiring: views raise events, this file decides what they mean
        Status.CancelClicked += (_, _) => _opCts?.Cancel();
        Actions.PrimaryClicked += async (_, _) =>
        {
            _retryTimer?.Stop();
            if (_pending is { } build) await RunGuardedAsync(() => InstallBuildAsync(build));
            else await RunGuardedAsync(CheckFlowAsync);
        };
        Settings.RecheckRequested += async (_, _) =>
        {
            _retryTimer?.Stop();
            await RunGuardedAsync(CheckFlowAsync);
        };
        Settings.AccentChanged += (_, _) => ApplyAccent();
        Settings.WhatsNewRequested += async (_, _) => await ShowCurrentNotesAsync();
        Actions.SkipClicked += (_, _) => SkipPendingBuild();
        Builds.InstallRequested += async (_, folder) =>
        {
            _retryTimer?.Stop();
            await RunGuardedAsync(() => InstallFolderAsync(folder));
        };

        ThemeService.ListenToSystemAccent(() =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_state.AccentColor == "system") ApplyAccent();
            });
        });

        // Periodic check while the app stays open
        _autoCheckTimer.Interval = AppConfig.AutoCheckInterval;
        _autoCheckTimer.Tick += async (_, _) =>
        {
            if (!_busy && _state.AutoUpdate)
                await RunGuardedAsync(CheckFlowAsync);
        };
        _autoCheckTimer.Start();

        Closed += (_, _) =>
        {
            _closed = true;
            _autoCheckTimer.Stop();
            _retryTimer?.Stop();
            _cts.Cancel();
        };

        Root.Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            await ShowJustUpdatedAsync(); // after an update: what changed (once)
            await RunGuardedAsync(CheckFlowAsync);
        };

        // F5 = Check again
        var refresh = new KeyboardAccelerator { Key = Windows.System.VirtualKey.F5 };
        refresh.Invoked += async (_, a) =>
        {
            a.Handled = true;
            if (!_busy) await RunGuardedAsync(CheckFlowAsync);
        };
        Root.KeyboardAccelerators.Add(refresh);
    }

    /// <summary>"Skip this build": remember it, so neither auto-update nor "Update now" offers it again.</summary>
    private void SkipPendingBuild()
    {
        if (_busy || _pendingIsSwitch || _pending is not { } build) return;
        _state.SkippedBuild = build.Folder;
        StateStore.Save(_state);
        _pending = null;
        Status.Set(Strings.SkippedTitle, Strings.SkippedDetail(build.Folder), Phase.Good);
        Actions.SetPrimary(updateWaiting: false);
    }

    private void ApplyAccent()
    {
        ThemeService.ApplyAccent(_state.AccentColor);
        Status.RefreshTheme();
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (Changes is null || Settings is null || Builds is null) return; // fires once while the XAML loads
        var i = sender.Items.IndexOf(sender.SelectedItem);
        Changes.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        Settings.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
        Builds.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        Actions.SetBusy(busy);
        Builds.SetBusy(busy);
        Settings.SetBusy(busy);
        SelfUpdateButton.IsEnabled = !busy;
    }

    private void ScheduleRetry()
    {
        if (_closed || !_state.AutoUpdate) return;
        _retryTimer?.Stop();
        _retryTimer = new DispatcherTimer { Interval = AppConfig.NetworkRetryDelay };
        _retryTimer.Tick += async (_, _) =>
        {
            _retryTimer?.Stop();
            if (!_busy && !_closed && _state.AutoUpdate)
                await RunGuardedAsync(CheckFlowAsync);
        };
        _retryTimer.Start();
    }

    /// <summary>Common wrapper: busy state, buttons, cancellation and error handling for every action.</summary>
    private async Task RunGuardedAsync(Func<Task> work)
    {
        if (_busy) return;
        _retryTimer?.Stop();
        _busy = true;
        SetBusy(true);
        Status.SetBar(true);
        Status.SetCancel(visible: false);

        _opCts?.Dispose();
        _opCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

        try
        {
            await work();
        }
        catch (OperationCanceledException) when (_closed)
        {
            // window closed
        }
        catch (OperationCanceledException) when (_opCts.IsCancellationRequested)
        {
            Status.Set(Strings.CancelledTitle, Strings.CancelledDetail, Phase.Warn);
        }
        catch (OperationCanceledException)
        {
            Status.Set(Strings.Timeout, Strings.TimeoutDetail, Phase.Bad);
            ScheduleRetry();
        }
        catch (HttpRequestException ex)
        {
            Status.Set(Strings.WentWrong, ex.Message, Phase.Bad);
            ScheduleRetry();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Status.Set(Strings.UpdateCancelled, Strings.UacDeclined, Phase.Warn);
        }
        catch (Exception ex)
        {
            Status.Set(Strings.WentWrong, ex.Message, Phase.Bad);
        }
        finally
        {
            _busy = false;
            if (!_closed)
            {
                Status.SetCancel(visible: false);
                Status.SetBar(false);
                SetBusy(false);
                Actions.SetPrimary(updateWaiting: _pending is not null, canSkip: _pending is not null && !_pendingIsSwitch);
                _ = RefreshChangesAsync();
            }
        }
    }
}
