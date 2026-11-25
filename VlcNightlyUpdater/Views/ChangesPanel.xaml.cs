using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VlcNightlyUpdater.Services;

namespace VlcNightlyUpdater.Views;

/// <summary>
/// Shows a commit list. Call exactly one of ShowNone / ShowLoading / Show / ShowError. The filter bar (text box + "hide
/// merges and translations") only appears with a list and re-draws it from the commits already loaded: no network.
/// </summary>
public sealed partial class ChangesPanel : UserControl
{
    private ChangeRange? _range;
    private Changelog? _log;

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
        _range = range;
        _log = log;
        GitLabLink.NavigateUri = new Uri(log.CompareUrl);
        GitLabLink.Visibility = Visibility.Visible;
        FilterBar.Visibility = log.Commits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => Render();
    private void NoiseCheck_Changed(object sender, RoutedEventArgs e) => Render();

    /// <summary>Draws the header and the commits that pass the filter.</summary>
    private void Render()
    {
        if (_range is not { } range || _log is not { } log) return;

        var hide = NoiseCheck.IsChecked == true;
        var filtering = CommitFilter.IsActive(FilterBox.Text, hide);
        var shown = log.Commits.Where(c => CommitFilter.Matches(c, FilterBox.Text, hide)).ToList();

        HeaderText.Text = range.Label;
        SubText.Text = Format.Join($"{range.FromFolder}  \u2192  {range.ToFolder}",
            filtering ? Strings.CommitsMatching(shown.Count, log.Commits.Count) : Strings.CommitCount(log.Total));

        CommitList.Children.Clear();
        if (log.Approximate) AddNote(Strings.ChangesApprox);
        if (log.Commits.Count == 0) AddNote(Strings.ChangesEmpty);
        else if (shown.Count == 0) AddNote(Strings.ChangesNoMatch);

        foreach (var c in shown) CommitList.Children.Add(CommitRow(c));

        if (log.Total > log.Commits.Count)
            AddNote(filtering ? Strings.ChangesFilterCapped(log.Commits.Count, log.Total) : Strings.ChangesCapped(log.Commits.Count, log.Total));
    }

    // ---------- helpers ----------

    private void Message(string header, string body)
    {
        _range = null;
        _log = null;
        FilterBar.Visibility = Visibility.Collapsed;
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
