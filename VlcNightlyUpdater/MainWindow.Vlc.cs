using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;
using VlcNightlyUpdater.Views;

namespace VlcNightlyUpdater;

/// <summary>Everything that asks the user a question or deals with the running VLC (close it / wait for it). Wording lives in Strings.cs.</summary>
public sealed partial class MainWindow
{
    /// <summary>Two-button dialog. Returns true if the main button was pressed.</summary>
    private async Task<bool> ConfirmAsync(string title, string body, string yes, bool yesIsDefault = false) =>
        await new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = body,
            PrimaryButtonText = yes,
            CloseButtonText = Strings.Cancel,
            DefaultButton = yesIsDefault ? ContentDialogButton.Primary : ContentDialogButton.Close
        }.ShowAsync() == ContentDialogResult.Primary;

    private Task<bool> ConfirmArchSwitchAsync(VlcArch from, VlcArch to) =>
        ConfirmAsync(Strings.SwitchDialogTitle(to), Strings.SwitchDialogBody(from, to), Strings.InstallArch(to));

    private enum VlcChoice { Close, Wait, Cancel }

    /// <summary>VLC is open: close it for me, wait until I close it, or cancel.</summary>
    private async Task<VlcChoice> AskVlcRunningAsync()
    {
        var result = await new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.VlcRunningTitle,
            Content = Strings.VlcRunningBody,
            PrimaryButtonText = Strings.CloseAndUpdate,
            SecondaryButtonText = Strings.WaitForClose,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary
        }.ShowAsync();

        return result switch
        {
            ContentDialogResult.Primary => VlcChoice.Close,
            ContentDialogResult.Secondary => VlcChoice.Wait,
            _ => VlcChoice.Cancel
        };
    }

    /// <summary>
    /// If VLC is open: ask (or, with "wait for VLC" on, don't ask), then either close it politely, wait for the user to
    /// close it, or give up. Offers a force-close only if VLC refuses to close. Returns (VLC is closed, VLC should be
    /// reopened afterwards). When the user closed VLC themselves we don't reopen it (unless "Open VLC after updating").
    /// </summary>
    private async Task<(bool Closed, bool WasRunning)> PrepareVlcAsync(BuildInfo build)
    {
        if (!VlcProcess.IsRunning()) return (true, false);

        var choice = _state.WaitForVlc ? VlcChoice.Wait : await AskVlcRunningAsync();
        if (choice == VlcChoice.Cancel) return (false, true);
        if (choice == VlcChoice.Wait) return (await WaitForVlcToCloseAsync(build), false);

        Status.Set(Strings.ClosingVlc, Strings.ClosingVlcDetail, Phase.Working);
        Status.SetBar(true);

        VlcProcess.CloseGently();
        if (await VlcProcess.WaitForExitAsync(polls: 20, delayMs: 500, Ct)) return (true, true); // up to 10s: it may be saving

        if (!await ConfirmAsync(Strings.VlcStuckTitle, Strings.VlcStuckBody, Strings.ForceClose))
            return (false, true);

        VlcProcess.Kill();
        return (await VlcProcess.WaitForExitAsync(polls: 10, delayMs: 300, Ct), true);
    }

    /// <summary>
    /// "Install when VLC closes": the installer is already downloaded and verified, so this only waits. Nothing is
    /// closed for the user. The status card's Cancel button stops the wait (returns false); closing the window too.
    /// </summary>
    private async Task<bool> WaitForVlcToCloseAsync(BuildInfo build)
    {
        Status.Set(Strings.WaitingForVlc, Strings.WaitingForVlcDetail(build.Folder), Phase.Working);
        Status.SetBar(true);
        Status.SetCancel(visible: true);
        try
        {
            do
            {
                while (VlcProcess.IsRunning()) await Task.Delay(AppConfig.VlcWaitPollMs, Ct);
                await Task.Delay(AppConfig.VlcWaitGraceMs, Ct); // let Windows release VLC's files
            }
            while (VlcProcess.IsRunning());                     // VLC was opened again meanwhile: keep waiting
            return true;
        }
        catch (OperationCanceledException) when (!_closed && _opCts?.IsCancellationRequested == true)
        {
            return false; // the user pressed Cancel
        }
        finally
        {
            Status.SetCancel(visible: false);
        }
    }
}
