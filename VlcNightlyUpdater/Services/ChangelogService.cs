using System.Globalization;
using System.Text.Json;

namespace VlcNightlyUpdater.Services;

public sealed record CommitInfo(string ShortId, string Title, string Author, DateTime? Date, string Url);

/// <summary>Which two nightlies to compare. Hashes are optional; without them we fall back to commit dates.</summary>
/// <param name="Pending">true = an update is waiting (installed -> latest); false = looking back at the last update.</param>
public sealed record ChangeRange(string FromFolder, string? FromHash, string ToFolder, string? ToHash, string Label, bool Pending = false);

public sealed record Changelog(
    IReadOnlyList<CommitInfo> Commits, int Total, string CompareUrl, bool Approximate);

/// <summary>Reads VLC's commit history from code.videolan.org (public GitLab API, no login needed).</summary>
public static class ChangelogService
{
    private const string Api = AppConfig.GitLabApi;
    private const string Web = AppConfig.GitLabWeb;
    private const int MaxShown = AppConfig.ChangelogMaxShown;

    public static async Task<Changelog> GetAsync(ChangeRange r, CancellationToken ct)
    {
        // Exact: every commit in the new build that isn't in the old one.
        if (!string.IsNullOrEmpty(r.FromHash) && !string.IsNullOrEmpty(r.ToHash))
        {
            try
            {
                var json = await Net.Http.GetStringAsync(
                    $"{Api}/compare?from={r.FromHash}&to={r.ToHash}", ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var all = ParseCommits(doc.RootElement.GetProperty("commits"));
                return Build(all, $"{Web}/-/compare/{r.FromHash}...{r.ToHash}", approximate: false);
            }
            catch (HttpRequestException) { /* hash unknown to GitLab, etc: fall through to dates */ }
            catch (JsonException) { }
            catch (KeyNotFoundException) { }
        }

        // Approximate: commits on master between the two build timestamps.
        var from = ParseFolder(r.FromFolder);
        var to = ParseFolder(r.ToFolder);
        if (from is null || to is null || to <= from)
            return new Changelog([], 0, $"{Web}/-/commits/master", Approximate: true);

        var url = $"{Api}/commits?ref_name=master&per_page=100" +
                  $"&since={Uri.EscapeDataString(from.Value.ToString("o"))}" +
                  $"&until={Uri.EscapeDataString(to.Value.ToString("o"))}";
        var body = await Net.Http.GetStringAsync(url, ct).ConfigureAwait(false);
        using var d2 = JsonDocument.Parse(body);
        return Build(ParseCommits(d2.RootElement), $"{Web}/-/commits/master", approximate: true);
    }

    private static Changelog Build(List<CommitInfo> all, string compareUrl, bool approximate)
    {
        var ordered = all.OrderByDescending(c => c.Date ?? DateTime.MinValue).ToList();
        return new Changelog(ordered.Take(MaxShown).ToList(), ordered.Count, compareUrl, approximate);
    }

    private static List<CommitInfo> ParseCommits(JsonElement array)
    {
        var list = new List<CommitInfo>();
        foreach (var c in array.EnumerateArray())
        {
            string Str(string name) =>
                c.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

            DateTime? date = DateTimeOffset.TryParse(Str("committed_date"), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dto) ? dto.LocalDateTime : null;

            list.Add(new CommitInfo(Str("short_id"), Str("title"), Str("author_name"), date, Str("web_url")));
        }
        return list;
    }

    /// <summary>Nightly folder names are build times in UTC: 20261021-0426.</summary>
    private static DateTime? ParseFolder(string folder) =>
        DateTime.TryParseExact(folder, "yyyyMMdd-HHmm", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
}
