using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VlcNightlyUpdater.Views;

public sealed partial class ToggleRow : UserControl
{
    private bool _quiet;

    /// <summary>Raised only when the USER flips the switch (not when code sets IsOn).</summary>
    public event EventHandler? Toggled;

    public ToggleRow() => InitializeComponent();

    public string Title { get => TitleText.Text; set => TitleText.Text = value; }

    public string Hint
    {
        get => HintText.Text;
        set
        {
            HintText.Text = value;
            HintText.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public bool IsOn
    {
        get => Switch.IsOn;
        set { _quiet = true; Switch.IsOn = value; _quiet = false; }
    }

    private void Switch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_quiet) Toggled?.Invoke(this, EventArgs.Empty);
    }
}
