using System.Text.RegularExpressions;

namespace VlcNightlyUpdater.Services;

public enum IntentKind { None, Open, Update, Skip }

/// <summary>
/// What a notification button asked for. The toast's buttons start this exe with a link like
/// "vlcnightlyupdater:update" or "vlcnightlyupdater:skip?build=20261021-0426" (registered by BackgroundCheck).
/// Anything can open such a link, so it is treated as untrusted: only the known actions are accepted, and the build
/// name must look exactly like a nightly folder. "Update" does nothing that opening the app wouldn't do anyway
/// (same checks, same verified installer, same prompts).
/// </summary>
public sealed record LaunchIntent(IntentKind Kind, string? Build = null)
{
    public static readonly LaunchIntent None = new(IntentKind.None);

    /// <summary>Set once by App at startup; MainWindow reads it.</summary>
    public static LaunchIntent Current { get; set; } = None;

    private static readonly Regex BuildName = new(@"^\d{8}-\d{4}$", RegexOptions.Compiled);

    public static LaunchIntent Parse(IEnumerable<string> args)
    {
        var prefix = AppConfig.Protocol + ":";
        foreach (var raw in args.Skip(1)) // the first argument is the exe itself
        {
            if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var rest = raw[prefix.Length..].TrimStart('/');
            var q = rest.IndexOf('?');
            var action = (q < 0 ? rest : rest[..q]).Trim('/').ToLowerInvariant();
            var build = q < 0 ? null : QueryValue(rest[(q + 1)..], "build");

            return action switch
            {
                "update" => new LaunchIntent(IntentKind.Update),
                "skip" when build is not null && BuildName.IsMatch(build) => new LaunchIntent(IntentKind.Skip, build),
                "open" => new LaunchIntent(IntentKind.Open),
                _ => new LaunchIntent(IntentKind.Open) // unknown link: just open the app, do nothing else
            };
        }
        return None;
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimEnd('/').Split('&'))
        {
            var i = pair.IndexOf('=');
            if (i > 0 && pair[..i].Equals(key, StringComparison.OrdinalIgnoreCase)) return Uri.UnescapeDataString(pair[(i + 1)..]);
        }
        return null;
    }

    /// <summary>
    /// "Skip this build" pressed on a notification while no window is open: remember it, and say so in a small
    /// notification. Only a build newer than the installed one can be skipped. Returns true if it was recorded.
    /// </summary>
    public static bool ApplySkip(string build)
    {
        var state = StateStore.Load();
        if (state.InstalledBuild is null || string.CompareOrdinal(build, state.InstalledBuild) <= 0) return false;

        state.SkippedBuild = build;
        StateStore.Save(state);
        BackgroundCheck.ShowToast("vlc-skip", "vlc", Strings.SkippedFromToastTitle, Strings.SkippedFromToast(build));
        return true;
    }
}
