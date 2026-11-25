using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace VlcNightlyUpdater.Views;

/// <summary>
/// One reusable release-notes dialog. Pass null as <paramref name="primary"/> for a notes-only dialog (just a close
/// button). Show it with ShowAsync(); the result is Primary when the user chose the main action.
/// </summary>
public sealed partial class WhatsNewDialog : ContentDialog
{
    public WhatsNewDialog(XamlRoot root, string heading, string range, string notes, string? primary, string close)
    {
        InitializeComponent();
        XamlRoot = root;

        HeadingText.Text = heading;
        RangeText.Text = range;
        NotesHost.Content = ChangelogRenderer.Build(notes);

        PrimaryButtonText = primary ?? "";
        CloseButtonText = close;
        DefaultButton = primary is null ? ContentDialogButton.Close : ContentDialogButton.Primary;

        try
        {
            var logo = Path.Combine(AppContext.BaseDirectory, "Assets", "app-logo.png");
            if (File.Exists(logo)) Logo.Source = new BitmapImage(new Uri(logo));
        }
        catch { /* the logo is decoration only */ }
    }
}
