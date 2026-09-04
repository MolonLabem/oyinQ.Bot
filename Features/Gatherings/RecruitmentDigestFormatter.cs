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
        string timeZoneId, DateTimeOffset now, string username)
    {
        var ranked = GatheringRecruitment.Rank(gatherings, now);
        var text = new StringBuilder("🎲 <b>Ищут игроков · ближайшие игры</b>\n");
        var today = CommunityTime.LocalDate(now, timeZoneId);
        var buttons = new List<InlineKeyboardButton[]>();
        foreach (var g in ranked.Take(7))
        {
            var game = GatheringGameSnapshotSerializer.Deserialize(g.GameSnapshotJson);
            var name = game.Name.Length > 100 ? game.Name[..100] + "…" : game.Name;
            var link = TelegramBotDeepLinks.BuildMainMiniApp(username, MiniAppStartParameter.ForGathering(communityKey, g.PublicId));
            var local = TimeZoneInfo.ConvertTime(g.StartsAtUtc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
            var date = DateOnly.FromDateTime(local.DateTime);
            var label = date == today ? "Сегодня" : date == today.AddDays(1) ? "Завтра" : date.ToString("dd.MM");
            text.Append($"\n<b>{WebUtility.HtmlEncode(name)}</b>\n{GatheringRecruitment.Describe(g).Text}\n{label} · {local:HH:mm}\n");
            buttons.Add([InlineKeyboardButton.WithUrl(name.Length > 45 ? name[..45] + "…" : name, link)]);
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
}
