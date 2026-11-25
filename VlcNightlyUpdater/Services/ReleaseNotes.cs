namespace VlcNightlyUpdater.Services;

/// <summary>
/// Release notes come from CHANGELOG.md (repo root), which every release OVERWRITES with only that version's notes.
/// CI publishes it as the top of the GitHub release body, followed by <see cref="EndMarker"/> and the install table,
/// so the in-app dialog (which reads the release body) and the release page always show the same text.
/// A copy of the file also ships next to the exe for the post-update "What's new" dialog.
/// </summary>
public static class ReleaseNotes
{
    /// <summary>CI writes this line between the changelog and the install instructions. Keep in sync with build.yml.</summary>
    public const string EndMarker = "<!-- end-changelog -->";

    /// <summary>The changelog part of a GitHub release body (everything before the marker; the whole body if there is none).</summary>
    public static string FromReleaseBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var cut = body.IndexOf(EndMarker, StringComparison.Ordinal);
        return (cut >= 0 ? body[..cut] : body).Trim();
    }

    /// <summary>The CHANGELOG.md that shipped with this copy, or "" if it is missing.</summary>
    public static string LoadBundled()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch { return ""; }
    }
}
