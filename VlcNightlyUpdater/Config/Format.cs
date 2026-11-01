using System.Globalization;

namespace VlcNightlyUpdater;

/// <summary>Tiny display helpers: dates, sizes, times. No UI and no state.</summary>
public static class Format
{
    public const string Dot = "  \u00B7  ";

    /// <summary>"20261021-0426" -> "Oct 21, 2026 · 04:26" (empty if it isn't a build folder name).</summary>
    public static string Pretty(string? folder) =>
        folder is not null &&
        DateTime.TryParseExact(folder, "yyyyMMdd-HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("MMM d, yyyy \u00B7 HH:mm", CultureInfo.InvariantCulture)
            : "";

    public static string Bytes(double b) =>
        b >= 1024 * 1024 * 1024 ? $"{b / 1024 / 1024 / 1024:0.0} GB"
        : b >= 1024 * 1024 ? $"{b / 1024 / 1024:0.0} MB"
        : b >= 1024 ? $"{b / 1024:0} KB"
        : $"{b:0} B";

    public static string Eta(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s"
        : $"{Math.Max(1, (int)t.TotalSeconds)}s";

    /// <summary>Joins the non-empty parts with a middle dot.</summary>
    public static string Join(params string?[] parts) =>
        string.Join(Dot, parts.Where(p => !string.IsNullOrEmpty(p)));
}
