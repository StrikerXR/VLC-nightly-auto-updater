using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater.Views;

/// <summary>Shows a commit list. Call exactly one of ShowNone / ShowLoading / Show / ShowError.</summary>
public sealed partial class ChangesPanel : UserControl
{
    public ChangesPanel()
    {
        InitializeComponent();
        ShowNone();
    }

    public void ShowNone() => Message("", Strings.ChangesNone);

    public void ShowLoading(ChangeRange range) => Message(range.Label, Strings.ChangesLoading);

    public void ShowError(string why) => Message(Strings.WhatsNew, Strings.ChangesFailed(why));

    public void Show(ChangeRange range, Changelog log)
    {
        HeaderText.Text = range.Label;
        SubText.Text = Format.Join($"{range.FromFolder}  \u2192  {range.ToFolder}", Strings.CommitCount(log.Total));
        GitLabLink.NavigateUri = new Uri(log.CompareUrl);
        GitLabLink.Visibility = Visibility.Visible;
        CommitList.Children.Clear();

        if (log.Approximate) AddNote(Strings.ChangesApprox);
        if (log.Commits.Count == 0) AddNote(Strings.ChangesEmpty);

        foreach (var c in log.Commits) CommitList.Children.Add(CommitRow(c));

        if (log.Total > log.Commits.Count) AddNote(Strings.ChangesCapped(log.Commits.Count, log.Total));
    }

    // ---------- helpers ----------

    private void Message(string header, string body)
    {
        HeaderText.Text = header;
        SubText.Text = "";
        GitLabLink.Visibility = Visibility.Collapsed;
        CommitList.Children.Clear();
        AddNote(body);
    }

    private void AddNote(string text) =>
        CommitList.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["VnuLabel"]
        });

    private static UIElement CommitRow(CommitInfo c)
    {
        var title = new TextBlock { Text = c.Title, TextWrapping = TextWrapping.Wrap };
        UIElement head = Uri.TryCreate(c.Url, UriKind.Absolute, out var uri)
            ? new HyperlinkButton { Padding = new Thickness(0), NavigateUri = uri, Content = title, HorizontalAlignment = HorizontalAlignment.Left }
            : title;

        var meta = Format.Join(c.ShortId, c.Author, c.Date?.ToString("MMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture));

        var row = new StackPanel { Spacing = 0 };
        row.Children.Add(head);
        row.Children.Add(new TextBlock { Text = meta, Style = (Style)Application.Current.Resources["VnuLabel"] });
        return row;
    }
}
