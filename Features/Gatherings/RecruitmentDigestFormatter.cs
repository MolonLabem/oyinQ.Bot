using oyinQ.Bot.Features.Communities;
using System.Net;
using System.Text;
using oyinQ.Bot.Data.Entities;
using oyinQ.Bot.Integrations.Telegram;
using Telegram.Bot.Types.ReplyMarkups;

namespace oyinQ.Bot.Features.Gatherings;

public sealed record RecruitmentDigestMessage(string Text, InlineKeyboardMarkup Keyboard, int Shown, int Total);

public static class RecruitmentDigestFormatter
{
    public static RecruitmentDigestMessage Build(IEnumerable<GameGathering> gatherings, string communityKey,
        string timeZoneId, DateTimeOffset now, string username, bool includeAllUpcoming = false)
    {
        var ranked = GatheringRecruitment.Rank(gatherings, now, includeAllUpcoming);
        var text = new StringBuilder("🎲 <b>Ищут игроков · ближайшие игры</b>\n");
        var today = CommunityTime.LocalDate(now, timeZoneId);
        var buttons = new List<InlineKeyboardButton[]>();
        var entries = ranked.Take(7).Select(g =>
        {
            var game = GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson);
            var name = Shorten(game.Name, 100);
            var local = TimeZoneInfo.ConvertTime(g.StartsAtUtc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
            var date = DateOnly.FromDateTime(local.DateTime);
            var label = date == today ? "Сегодня" : date == today.AddDays(1) ? "Завтра" : date.ToString("dd.MM");
            return new { Gathering = g, Name = name, Date = $"{label} · {local:HH:mm}", Button = $"{label} {local:HH:mm} · {Shorten(name, 32)}" };
        }).ToArray();
        var duplicates = entries.GroupBy(x => x.Button).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet();
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            // Simultaneous tables (or truncated titles) get the same identifier in body and button.
            var suffix = duplicates.Contains(entry.Button) ? $" · №{index + 1}" : "";
            var link = TelegramBotDeepLinks.BuildMainMiniApp(username, MiniAppStartParameter.ForGathering(communityKey, entry.Gathering.PublicId));
            text.Append($"\n<b>{WebUtility.HtmlEncode(entry.Name + suffix)}</b>\n{GatheringRecruitment.Describe(entry.Gathering).Text}\n{entry.Date}\n");
            buttons.Add([InlineKeyboardButton.WithUrl(entry.Button + suffix, link)]);
        }
        var shown = buttons.Count;
        if (ranked.Count > shown)
        {
            var count = ranked.Count - shown;
            var noun = count % 100 is >= 11 and <= 14 ? "сборов" : count % 10 == 1 ? "сбор" : count % 10 is >= 2 and <= 4 ? "сбора" : "сборов";
            text.Append($"\nЕщё {count} {noun}\n");
        }
        buttons.Add([InlineKeyboardButton.WithUrl("Посмотреть все сборы",
            TelegramBotDeepLinks.BuildMainMiniApp(username, MiniAppStartParameter.ForCommunity(communityKey)))]);
        return new(text.ToString(), new InlineKeyboardMarkup(buttons), shown, ranked.Count);
    }

    private static string Shorten(string value, int length) => value.Length <= length ? value
        : value[..(char.IsHighSurrogate(value[length - 1]) ? length - 1 : length)] + "…";
}
