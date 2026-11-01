using VlcNightlyUpdater.Services;
using VlcNightlyUpdater.Views;

namespace VlcNightlyUpdater;

/// <summary>Getting a verified installer, running it, and recording the result. Progress text comes from Strings.cs.</summary>
public sealed partial class MainWindow
{
    /// <summary>Install one specific nightly chosen in the Builds tab (works offline if it is cached).</summary>
    private async Task InstallFolderAsync(string folder)
    {
        Status.Set(Strings.Preparing, folder, Phase.Working);

        BuildInfo? build = null;
        try { build = await _updates.ResolveAsync(folder, Ct); }
        catch (HttpRequestException) { /* offline: the cache may still have it */ }
        build ??= InstallerCache.TryGetBuild(folder, _target);

        await InstallBuildAsync(build ?? throw new InvalidOperationException(Strings.NoExeForBuild));
    }

    private async Task InstallBuildAsync(BuildInfo build)
    {
        _pending = null;
        try
        {
            await InstallCoreAsync(build);
        }
        catch
        {
            _pending = build; // let "Update now" retry without another network check
            throw;
        }
    }

    private async Task InstallCoreAsync(BuildInfo build)
    {
        var current = VlcLocator.Find();
        if (current?.Arch is { } have && have != build.Arch && !await ConfirmArchSwitchAsync(have, build.Arch))
        {
            Status.Set(Strings.CancelledTitle, Strings.LeftAsIs, Phase.Warn);
            _pending = build;
            return;
        }

        // Get the installer first while VLC keeps running: only close it right before installing.
        var installer = await EnsureInstallerAsync(build);

        var (closed, wasRunning) = await PrepareVlcAsync();
        if (!closed)
        {
            Status.Set(Strings.UpdateCancelled, Strings.VlcStillRunning, Phase.Warn);
            _pending = build;
            return;
        }

        var installed = false;
        try
        {
            Status.Set(Strings.Installing, Strings.InstallingDetail, Phase.Working);
            Status.SetBar(true);

            var code = await UpdateService.InstallAsync(installer);
            if (code != 0)
            {
                Status.Set(Strings.Retrying, Strings.RetryDetail(code), Phase.Working);
                await Task.Delay(2000, Ct);
                code = await UpdateService.InstallAsync(installer);
            }
            if (code != 0) throw new InvalidOperationException(Strings.InstallFailed(code));

            installed = true;
        }
        finally
        {
            // If we closed VLC and then the install failed or UAC was declined, put the user's VLC back.
            if (!installed && wasRunning) VlcProcess.Launch();
        }

        // Make sure it really landed, and as the architecture we meant to install.
        var after = VlcLocator.Find(build.Arch)
                    ?? throw new InvalidOperationException(Strings.NoVlcAfter);
        var actualArch = after.Arch ?? build.Arch;

        if (_state.InstalledBuild != build.Folder)
        {
            _state.PreviousBuild = _state.InstalledBuild;
            _state.PreviousHash = _state.InstalledHash;
        }
        _state.InstalledBuild = build.Folder;
        _state.InstalledHash = build.Hash;
        _state.InstalledArch = actualArch.Key();
        StateStore.Save(_state);

        InstallerCache.Prune(build.Arch, protect: installer); // keep the last few for offline rollback
        Builds.Refresh();
        Tiles.SetInstalled(build.Folder, actualArch);

        var note = actualArch != build.Arch ? Strings.ArchNote(actualArch, build.Arch) : "";
        Status.Set(Strings.Updated, Strings.UpdatedDetail(build.Folder, actualArch, note), Phase.Good);

        if (_state.LaunchAfterUpdate || wasRunning) VlcProcess.Launch();
    }

    private async Task<string> EnsureInstallerAsync(BuildInfo build)
    {
        var stage = new Progress<InstallStage>(s =>
        {
            if (_closed) return;
            switch (s)
            {
                case InstallStage.UsingCache:
                    Status.SetCancel(visible: false);
                    Status.Set(Strings.CheckingCache, build.ExeName, Phase.Working);
                    Status.SetBar(true);
                    break;
                case InstallStage.Downloading:
                    Status.Set(Strings.Downloading + "\u2026", build.ExeName, Phase.Download);
                    Status.SetBar(true, indeterminate: false);
                    Status.SetCancel(visible: true);
                    break;
                case InstallStage.Verifying:
                    Status.SetCancel(visible: false);
                    Status.Set(Strings.Verifying, Strings.VerifyingDetail, Phase.Working);
                    Status.SetBar(true);
                    break;
            }
        });

        try
        {
            return await _updates.EnsureInstallerAsync(build, MakeProgress(Strings.Downloading), stage, Ct);
        }
        finally
        {
            Status.SetCancel(visible: false);
        }
    }

    /// <summary>Turns download progress into the status line, bar and "12 MB/s · 1m 04s left" detail.</summary>
    private IProgress<DownloadProgress> MakeProgress(string title) => new Progress<DownloadProgress>(p =>
    {
        if (_closed) return;

        if (p.Note is not null)
        {
            Status.SetTitle(Strings.Reconnecting);
            Status.SetDetail(p.Note);
            Status.SetIndeterminate();
            return;
        }

        if (p.Total > 0) Status.SetPercent(p.Percent); else Status.SetIndeterminate();
        Status.SetTitle(p.Total > 0 ? $"{title}\u2026 {p.Percent:0}%" : $"{title}\u2026");

        Status.SetDetail(Format.Join(
            p.Total > 0 ? $"{Format.Bytes(p.Received)} of {Format.Bytes(p.Total)}" : Format.Bytes(p.Received),
            p.BytesPerSecond > 0 ? $"{Format.Bytes(p.BytesPerSecond)}/s" : null,
            p.Eta is { } eta ? $"{Format.Eta(eta)} left" : null));
    });
}
