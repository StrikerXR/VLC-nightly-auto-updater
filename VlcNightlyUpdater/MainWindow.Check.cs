using VlcNightlyUpdater.Services;
using VlcNightlyUpdater.Views;

namespace VlcNightlyUpdater;

/// <summary>The "check for updates" flow: this updater first, then VLC. Decides what the status card says.</summary>
public sealed partial class MainWindow
{
    private (VlcInstall? Install, VlcArch Target) ResolveTarget()
    {
        var inst = VlcLocator.Find();
        var target = ArchInfo.ParseKey(_state.ArchOverride) ?? inst?.Arch ?? ArchInfo.Native;
        if (!ArchInfo.Supported.Contains(target)) target = ArchInfo.Native;
        return (inst, target);
    }

    private async Task CheckFlowAsync()
    {
        _pending = null;
        _pendingIsSwitch = false;

        if (Environment.OSVersion.Version.Build < AppConfig.MinWindowsBuild)
        {
            Status.Set(Strings.OsTooOld, Strings.OsTooOldDetail, Phase.Bad);
            return;
        }

        Status.Set(Strings.Checking, Strings.CheckingDetail, Phase.Working);

        // 1) This updater first. If it replaced itself, this process is exiting.
        if (await SelfUpdateCheckAsync()) return;

        // 2) Then VLC.
        var (vlc, target) = ResolveTarget();
        _target = target;
        _updates.Arch = target;

        Status.Set(Strings.CheckingVlc, Strings.ChannelDetail(target), Phase.Working);
        var folders = await _updates.ListFoldersAsync(Ct);
        var latest = await _updates.GetLatestAsync(folders, Ct);
        _latest = latest;

        _state.LastChecked = DateTime.Now;
        Builds.SetBuilds(folders, target);
        Tiles.SetInstalled(_state.InstalledBuild, vlc?.Arch ?? ArchInfo.ParseKey(_state.InstalledArch));
        Tiles.SetLatest(latest);

        // First run with VLC already installed: we don't know which nightly it is, so ask (see MainWindow.FirstRun.cs).
        if (_state.InstalledBuild is null && vlc is not null && (vlc.Arch is null || vlc.Arch == target))
        {
            StateStore.Save(_state);
            Actions.SetLastChecked(_state.LastChecked);

            if (!await AskIsLatestAsync(latest, target))
            {
                await InstallBuildAsync(latest); // "No" and "I don't know": install the newest to be safe
                return;
            }

            _state.InstalledBuild = latest.Folder;
            _state.InstalledHash = latest.Hash;
            _state.InstalledArch = target.Key();
            StateStore.Save(_state);
            Tiles.SetInstalled(latest.Folder, target);
            Status.Set(Strings.FirstRun, Strings.FirstRunDetail, Phase.Good);
            return;
        }

        StateStore.Save(_state);
        Actions.SetLastChecked(_state.LastChecked);

        var archSwitch = vlc?.Arch is { } have && have != target;

        if (vlc is not null && !archSwitch && string.CompareOrdinal(_state.InstalledBuild, latest.Folder) >= 0)
        {
            Status.Set(Strings.UpToDate, Strings.UpToDateDetail(target), Phase.Good);
            return;
        }

        // Never switch CPU type behind the user's back, even with auto-update on.
        if (archSwitch)
        {
            _pending = latest;
            _pendingIsSwitch = true;
            Status.Set(Strings.SwitchBuild, Strings.SwitchDetail(vlc!.Arch!.Value, target, latest.Folder), Phase.Warn);
            return;
        }

        // The user chose to skip exactly this nightly: stay put until a newer one appears.
        if (_state.SkippedBuild == latest.Folder)
        {
            Status.Set(Strings.SkippedTitle, Strings.SkippedDetail(latest.Folder), Phase.Good);
            return;
        }

        if (!_state.AutoUpdate)
        {
            _pending = latest;
            Status.Set(Strings.UpdateAvailable, Strings.UpdateDetail(_state.InstalledBuild, latest.Folder), Phase.Warn);
            return;
        }

        await InstallBuildAsync(latest);
    }
}
