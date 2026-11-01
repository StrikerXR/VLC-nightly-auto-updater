using VlcNightlyUpdater.Services;
using VlcNightlyUpdater.Views;

namespace VlcNightlyUpdater;

/// <summary>
/// The "Changes" tab and the BEHIND tile: which two nightlies to compare, and loading the commits between them.
/// Runs by itself after every action (see RunGuardedAsync); skips the network if the range hasn't changed.
/// </summary>
public sealed partial class MainWindow
{
    private string? _changesKey; // "from>to" of what is currently shown

    /// <summary>The two builds worth comparing right now, or null if there's nothing to show.</summary>
    private ChangeRange? CurrentRange()
    {
        var installed = _state.InstalledBuild;

        // Update waiting: installed -> latest.
        if (installed is not null && _latest is { } lat && lat.Arch == _target &&
            string.CompareOrdinal(installed, lat.Folder) < 0)
            return new ChangeRange(installed, _state.InstalledHash, lat.Folder, lat.Hash, Strings.WhatsNew, Pending: true);

        // Up to date: show what the last update brought.
        if (installed is not null && _state.PreviousBuild is { } prev &&
            string.CompareOrdinal(prev, installed) < 0)
            return new ChangeRange(prev, _state.PreviousHash, installed, _state.InstalledHash, Strings.LastUpdate);

        return null;
    }

    private async Task RefreshChangesAsync()
    {
        var range = CurrentRange();
        if (range is null)
        {
            _changesKey = null;
            Changes.ShowNone();
            if (_state.InstalledBuild is not null && _latest is not null) Tiles.SetBehind("0", Strings.BehindNone);
            else Tiles.SetBehind("\u2014", "");
            return;
        }

        var key = $"{range.FromFolder}>{range.ToFolder}";
        if (key == _changesKey) return; // already showing this one
        _changesKey = key;

        Changes.ShowLoading(range);
        Tiles.SetBehind("\u2026", range.Pending ? Strings.BehindCaption : "");

        try
        {
            var log = await ChangelogService.GetAsync(range, _cts.Token);
            if (_closed || key != _changesKey) return; // window closed, or a newer range replaced this one

            Changes.Show(range, log);
            Tiles.SetBehind(range.Pending ? log.Total.ToString() : "0", range.Pending ? Strings.BehindCaption : Strings.BehindNone);
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex)
        {
            if (_closed) return;
            _changesKey = null; // let the next refresh try again
            Changes.ShowError(ex.Message);
            Tiles.SetBehind("\u2014", "");
        }
    }
}
