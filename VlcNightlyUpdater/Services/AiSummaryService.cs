using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VlcNightlyUpdater.Services;

/// <summary>
/// EXPERIMENTAL, opt-in. Turns a list of VLC commit titles into three plain-language bullets using Google's Gemini API.
/// This class is only reached when the user switched the feature on AND saved a key; with the switch off the app
/// never makes a request to Google. Only public commit titles are sent (no paths, no usernames, no PC info).
/// </summary>
public static class AiSummaryService
{
    public const string DefaultModel = "gemini-3.8-flash";

    private const int MaxCommits = 200;
    private const int MaxChars = 14_000;

    // ---------- key storage (DPAPI, current Windows user) ----------

    public static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public static string? Unprotect(string? blob)
    {
        if (string.IsNullOrWhiteSpace(blob)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(blob), null, DataProtectionScope.CurrentUser)); }
        catch { return null; } // different user/PC or corrupt: treat as "no key"
    }

    // ---------- the call ----------

    public static async Task<string> SummarizeAsync(
        string apiKey, string model, IReadOnlyList<CommitInfo> commits, int total, CancellationToken ct)
    {
        model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        if (!Regex.IsMatch(model, @"^[A-Za-z0-9._-]+$"))
            throw new InvalidOperationException("The model name has odd characters in it.");

        var list = new StringBuilder();
        foreach (var c in commits.Take(MaxCommits))
        {
            if (list.Length + c.Title.Length > MaxChars) break;
            list.Append("- ").AppendLine(c.Title);
        }

        var prompt =
            "Summarize these VLC code commits into 3 bullet points in simple, non-technical terms for a regular user. " +
            "Plain text only, one short sentence per bullet, each starting with \"- \". No intro, no outro." +
            (total > commits.Count ? $"\n(The list shows the newest {commits.Count} of {total} commits.)" : "") +
            "\n\nCommits:\n" + list;

        var body = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.3, maxOutputTokens = 1024 }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
        { Content = JsonContent.Create(body) };
        req.Headers.Add("x-goog-api-key", apiKey); // header, not URL: keeps the key out of logs

        using var resp = await Net.Http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(ErrorMessage(json) ?? $"Gemini answered {(int)resp.StatusCode}.");

        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        if (doc.RootElement.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0 &&
            cands[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var t) && t.GetString() is { } text) sb.Append(text);

        var clean = Clean(sb.ToString());
        if (clean.Length == 0) throw new InvalidOperationException("Gemini sent back an empty answer.");
        return clean;
    }

    private static string? ErrorMessage(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            return d.RootElement.GetProperty("error").GetProperty("message").GetString();
        }
        catch { return null; }
    }

    /// <summary>Strips markdown noise and normalises bullets to "•".</summary>
    private static string Clean(string raw)
    {
        var lines = raw.Replace("**", "").Replace("`", "").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => Regex.Replace(l, @"^([-*•]|\d+[.)])\s*", "• "));
        return string.Join("\n", lines);
    }
}
