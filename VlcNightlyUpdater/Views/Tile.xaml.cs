using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace VlcNightlyUpdater.Views;

/// <summary>One stat card. Clicking it copies the value (a build name) to the clipboard and says so for a moment.</summary>
public sealed partial class Tile : UserControl
{
    public Tile()
    {
        InitializeComponent();
        Tapped += Tile_Tapped;
        ToolTipService.SetToolTip(this, Strings.ClickToCopy);
    }

    /// <summary>Small caps label above the value. Set from XAML: &lt;v:Tile Label="INSTALLED" /&gt;.</summary>
    public string Label { get => LabelText.Text; set => LabelText.Text = value; }
    /// <summary>Segoe Fluent Icons glyph in the chip. Set from XAML: Glyph="&amp;#xE74C;".</summary>
    public string Glyph { get => GlyphIcon.Glyph; set => GlyphIcon.Glyph = value; }
    public string Value { get => ValueText.Text; set => ValueText.Text = value; }
    public string Sub { get => SubText.Text; set => SubText.Text = value; }

    private async void Tile_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var text = ValueText.Text;
        if (string.IsNullOrWhiteSpace(text) || text == "\u2014") return;
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch { return; /* clipboard busy: say nothing */ }

        var old = SubText.Text;
        SubText.Text = Strings.Copied;
        await Task.Delay(1500);
        if (SubText.Text == Strings.Copied) SubText.Text = old;
    }
}
