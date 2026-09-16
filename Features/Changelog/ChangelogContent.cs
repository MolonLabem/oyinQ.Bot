using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace oyinQ.Bot.Features.Changelog;

public sealed record ChangelogRelease(string Id, string Date, string Text);

public static class ChangelogContent
{
    public const int MaxAnnouncementLength = 3500;
    private static readonly Lazy<string> Document = new(() =>
    {
        using var stream = typeof(ChangelogContent).Assembly.GetManifestResourceStream("OyinQ.Changelog")
            ?? throw new InvalidOperationException("Раздел «Что нового?» временно недоступен.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
    private static readonly Lazy<ChangelogRelease> Current = new(() => LatestFrom(Markdown));
    public static string Markdown => Document.Value;
    public static ChangelogRelease Latest => Current.Value;

    internal static ChangelogRelease LatestFrom(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var headings = Regex.Matches(normalized, @"^## (?<date>[^\n]+)$", RegexOptions.Multiline);
        var latest = headings.Select((match, index) => new { Match = match, Index = index,
                Date = DateOnly.TryParseExact(match.Groups["date"].Value.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateOnly?)null })
            .Where(x => x.Date.HasValue).OrderByDescending(x => x.Date).FirstOrDefault()
            ?? throw new InvalidOperationException("В журнале изменений нет датированного выпуска.");
        var start = latest.Match.Index + latest.Match.Length;
        var end = latest.Index + 1 < headings.Count ? headings[latest.Index + 1].Index : normalized.Length;
        var body = normalized[start..end].Trim();
        if (string.IsNullOrWhiteSpace(body)) throw new InvalidOperationException("Последний выпуск журнала изменений пуст.");
        // The changelog's supported headings, bullets and inline code become plain Telegram text.
        body = Regex.Replace(body, @"^#{3,6} +", "", RegexOptions.Multiline);
        body = Regex.Replace(body, @"^- +", "• ", RegexOptions.Multiline);
        body = Regex.Replace(body, @"`([^`\n]+)`", "$1");
        var releaseDate = latest.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var text = $"🎲 Что нового в OyinQ · {releaseDate}\n\n{body}";
        if (text.Length > MaxAnnouncementLength)
            throw new InvalidOperationException("Последний выпуск слишком длинный для рассылки. Сократите запись в CHANGELOG.md.");
        // Same-day edits require a fresh review; unchanged content keeps delivery deduplication.
        var revision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
        return new($"{releaseDate}-{revision}", releaseDate, text);
    }
}
