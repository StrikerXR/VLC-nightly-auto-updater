using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater.Views;

public sealed partial class ActionBar : UserControl
{
    /// <summary>The main button was pressed (it means "Check again" or "Update now"; ask MainWindow which).</summary>
    public event EventHandler? PrimaryClicked;

    public ActionBar() => InitializeComponent();

    /// <summary>true = the main button reads "Update now", false = "Check again".</summary>
    public void SetPrimary(bool updateWaiting)
    {
        PrimaryText.Text = updateWaiting ? "Update now" : "Check again";
        PrimaryIcon.Glyph = updateWaiting ? Icons.Download : Icons.Refresh;
    }

    public void SetBusy(bool busy)
    {
        PrimaryButton.IsEnabled = !busy;
        LaunchButton.IsEnabled = !busy && VlcLocator.FindVlcExe() is not null;
    }

    public void SetLastChecked(DateTime? t) => LastCheckedText.Text = t is { } v ? Strings.LastChecked(v) : "";

    private void PrimaryButton_Click(object sender, RoutedEventArgs e) => PrimaryClicked?.Invoke(this, EventArgs.Empty);
    private void LaunchButton_Click(object sender, RoutedEventArgs e) => VlcProcess.Launch();
    private void ConfigButton_Click(object sender, RoutedEventArgs e) => VlcProcess.OpenConfigFolder();
}
