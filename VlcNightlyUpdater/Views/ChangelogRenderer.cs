using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace VlcNightlyUpdater.Views;

/// <summary>
/// Draws the small subset of Markdown that CHANGELOG.md uses: "# title" (skipped, the dialog has its own header),
/// "## section", "### subsection", "- bullet", plain paragraphs, and inline **bold** / `code`. Links keep only their text.
/// To support more Markdown, add a case in <see cref="Build"/>; keep CHANGELOG.md simple instead if you can.
/// </summary>
public static class ChangelogRenderer
{
    private static readonly Regex Inline = new(@"(\*\*.+?\*\*|`.+?`)", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Comment = new(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

    public static StackPanel Build(string markdown)
    {
        var host = new StackPanel { Spacing = 6 };
        var text = Comment.Replace(markdown ?? "", "");
        var sawTitle = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').TrimEnd();
            if (line.Length == 0 || line == "---") continue;

            if (line.StartsWith("# "))
            {
                if (!sawTitle) { sawTitle = true; continue; } // the dialog header already says the version
                host.Children.Add(Block(line[2..], "SubtitleTextBlockStyle", 8));
            }
            else if (line.StartsWith("## "))
                host.Children.Add(Section(line[3..]));
            else if (line.StartsWith("### "))
                host.Children.Add(Block(line[4..], "BodyStrongTextBlockStyle", 6));
            else if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
                host.Children.Add(Bullet(line.TrimStart()[2..]));
            else
                host.Children.Add(Paragraph(line));
            sawTitle = true;
        }
        return host;
    }

    private static TextBlock Section(string title)
    {
        var t = Block(title, "BodyStrongTextBlockStyle", 14);
        t.Foreground = (Brush)Application.Current.Resources["VnuBrandBrush"];
        return t;
    }

    private static TextBlock Block(string text, string style, double top)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
        t.Style = (Style)Application.Current.Resources[style];
        Fill(t, text);
        return t;
    }

    private static TextBlock Paragraph(string text)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = (double)Application.Current.Resources["VnuMutedOpacity"] };
        Fill(t, text);
        return t;
    }

    private static Grid Bullet(string text)
    {
        var g = new Grid { ColumnSpacing = 10 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new Border
        {
            Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(2, 8, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = (Brush)Application.Current.Resources["VnuBrandBrush"]
        };
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Fill(t, text);
        Grid.SetColumn(t, 1);
        g.Children.Add(dot);
        g.Children.Add(t);
        return g;
    }

    /// <summary>Puts text into a TextBlock, turning **bold** and `code` into styled runs.</summary>
    private static void Fill(TextBlock target, string text)
    {
        text = Link.Replace(text, "$1");
        var pos = 0;
        foreach (Match m in Inline.Matches(text))
        {
            if (m.Index > pos) target.Inlines.Add(new Run { Text = text[pos..m.Index] });
            var token = m.Value;
            if (token.StartsWith("**"))
                target.Inlines.Add(new Run { Text = token[2..^2], FontWeight = FontWeights.SemiBold });
            else
                target.Inlines.Add(new Run { Text = token[1..^1], FontFamily = new FontFamily("Consolas") });
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) target.Inlines.Add(new Run { Text = text[pos..] });
    }
}
