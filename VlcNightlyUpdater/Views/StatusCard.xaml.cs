using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VlcNightlyUpdater.Views;

/// <summary>Headline + detail + progress. MainWindow only ever calls these methods; it never touches the controls.</summary>
public sealed partial class StatusCard : UserControl
{
    private Phase _phase = Phase.Working;

    public event EventHandler? CancelClicked;

    public StatusCard() => InitializeComponent();

    /// <summary>Sets headline, detail line and the icon/color for the phase.</summary>
    public void Set(string title, string? detail, Phase phase)
    {
        _phase = phase;
        TitleText.Text = title;
        SetDetail(detail);
        StatusIcon.Glyph = phase switch
        {
            Phase.Working => Icons.Working,
            Phase.Download => Icons.Download,
            Phase.Good => Icons.Good,
            Phase.Warn => Icons.Warn,
            _ => Icons.Bad
        };
        ApplyPhaseBrushes();
    }

    public void RefreshTheme() => ApplyPhaseBrushes();

    private void ApplyPhaseBrushes()
    {
        StatusIcon.Foreground = (Brush)Application.Current.Resources[$"VnuPhase{_phase}Brush"];
        IconBadge.Background = (Brush)Application.Current.Resources[$"VnuPhase{_phase}TintBrush"];
        Bar.Foreground = (Brush)Application.Current.Resources["VnuBrandBrush"];
    }

    public void SetTitle(string title) => TitleText.Text = title;

    public void SetDetail(string? detail)
    {
        DetailText.Text = detail ?? "";
        DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Shows/hides the bar (layout stays stable). Indeterminate = "working, no percentage".</summary>
    public void SetBar(bool active, bool indeterminate = true)
    {
        Bar.IsIndeterminate = active && indeterminate;
        if (!active) Bar.Value = 0;
        Bar.Opacity = active ? 1 : 0;
    }

    public void SetPercent(double percent) { Bar.IsIndeterminate = false; Bar.Value = percent; }
    public void SetIndeterminate() => Bar.IsIndeterminate = true;

    public void SetCancel(bool visible, bool enabled = true)
    {
        CancelButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = enabled;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        CancelClicked?.Invoke(this, EventArgs.Empty);
    }
}
