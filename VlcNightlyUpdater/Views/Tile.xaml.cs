using Microsoft.UI.Xaml.Controls;

namespace VlcNightlyUpdater.Views;

public sealed partial class Tile : UserControl
{
    public Tile() => InitializeComponent();

    /// <summary>Small caps label above the value. Set from XAML: &lt;v:Tile Label="INSTALLED" /&gt;.</summary>
    public string Label { get => LabelText.Text; set => LabelText.Text = value; }
    public string Value { get => ValueText.Text; set => ValueText.Text = value; }
    public string Sub { get => SubText.Text; set => SubText.Text = value; }
}
