using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;
using VlcNightlyUpdater.Views;

namespace VlcNightlyUpdater;

/// <summary>Everything that asks the user a question or deals with the running VLC. Wording lives in Strings.cs.</summary>
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

    /// <summary>
    /// If VLC is open: ask, close it politely, wait for it to actually exit, and only offer a force-close if it
    /// refuses. Returns (VLC is closed, VLC had been running).
    /// </summary>
    private async Task<(bool Closed, bool WasRunning)> PrepareVlcAsync()
    {
        if (!VlcProcess.IsRunning()) return (true, false);

        if (!await ConfirmAsync(Strings.VlcRunningTitle, Strings.VlcRunningBody, Strings.CloseAndUpdate, yesIsDefault: true))
            return (false, true);

        Status.Set(Strings.ClosingVlc, Strings.ClosingVlcDetail, Phase.Working);
        Status.SetBar(true);

        VlcProcess.CloseGently();
        if (await VlcProcess.WaitForExitAsync(polls: 20, delayMs: 500, Ct)) return (true, true); // up to 10s: it may be saving

        if (!await ConfirmAsync(Strings.VlcStuckTitle, Strings.VlcStuckBody, Strings.ForceClose))
            return (false, true);

        VlcProcess.Kill();
        return (await VlcProcess.WaitForExitAsync(polls: 10, delayMs: 300, Ct), true);
    }
}
