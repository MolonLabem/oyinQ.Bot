using System.Text;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace oyinQ.Bot.Features.Admin;

public sealed record CampParticipantMessage(string Text, InputRichMessage RichMessage, int ParticipantCount);

public static class CampParticipantMessages
{
    // A conservative shared budget also stays within rich-message limits:
    // 32768 UTF-8 characters, 500 blocks (including rows), and 20 columns.
    private const int TextBudget = 3900;
    private const int RowBudget = 80;

    public static IReadOnlyList<CampParticipantMessage> Build(CampAdminParticipants roster)
    {
        var heading = $"⛺ {roster.CampName}\nУчастников: {roster.Participants.Count} из {roster.TotalCount}\n{roster.FilterDescription}";
        var chunks = new List<CampParticipantMessage>();
        var rows = new List<RichBlockTableCell[]>();
        var text = new StringBuilder();
        StartChunk();
        for (var index = 0; index < roster.Participants.Count; index++)
        {
            var person = roster.Participants[index];
            var name = $"{index + 1}. {person.DisplayName}\n{person.City ?? "Город не указан"}"
                + (person.TelegramUsername is { } username ? $"\n@{username}" : "");
            var dates = $"{person.DatesText}\nДней: {person.DayCount}";
            var rowText = $"\n\n{name}\nДаты: {dates}\nЖильё: {person.AccommodationText}";
            if (Encoding.UTF8.GetByteCount(heading + rowText) > TextBudget)
                throw new InvalidOperationException("Одна из строк слишком длинная для сообщения. Отправьте список файлом Excel или CSV.");
            if (rows.Count > 1 && (Encoding.UTF8.GetByteCount(text.ToString() + rowText) > TextBudget || rows.Count > RowBudget))
            {
                FinishChunk();
                StartChunk();
            }
            text.Append(rowText);
            // RichText strings are plain text, escaped by the SDK's JSON serializer.
            rows.Add([Cell(name), Cell(dates), Cell(person.AccommodationText)]);
        }
        if (roster.Participants.Count == 0) text.Append("\n\nПо выбранным условиям участников нет.");
        FinishChunk();
        return chunks;

        void StartChunk()
        {
            text.Clear().Append(heading);
            rows = [[Cell("Участник", true), Cell("Даты", true), Cell("Жильё", true)]];
        }
        void FinishChunk() => chunks.Add(new(text.ToString(), new InputRichMessage
        {
            Blocks = rows.Count == 1
                ? [new InputRichBlockParagraph { Text = Plain(text.ToString()) }]
                : [new InputRichBlockParagraph { Text = Plain(heading) }, new InputRichBlockTable { Cells = rows.ToArray(), IsBordered = true, IsStriped = true }],
            SkipEntityDetection = true
        }, rows.Count - 1));
    }

    private static RichText Plain(string value) => new RichTextText { Text = value };
    private static RichBlockTableCell Cell(string text, bool header = false) => new()
    {
        Text = Plain(text), IsHeader = header, Align = RichBlockTableCellAlign.Left, Valign = RichBlockTableCellValign.Top
    };

    public static bool IsUnsupported(ApiRequestException exception) =>
        (exception.ErrorCode == 404 && exception.Message.Contains("method not found", StringComparison.OrdinalIgnoreCase))
        || (exception.ErrorCode == 400 && (exception.Message.Contains("RICH_MESSAGES_NOT_SUPPORTED", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("RICH_MESSAGE_NOT_SUPPORTED", StringComparison.OrdinalIgnoreCase)
            || (exception.Message.Contains("rich message", StringComparison.OrdinalIgnoreCase)
                && exception.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase))));
}
