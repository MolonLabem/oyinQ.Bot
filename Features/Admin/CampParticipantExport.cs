using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using oyinQ.Bot.Features.Communities;

namespace oyinQ.Bot.Features.Admin;

public sealed record CampParticipantExportFile(string FileName, string ContentType, byte[] Content, int ParticipantCount);

public static class CampParticipantExport
{
    public static readonly string[] Headers = ["№", "Участник", "Telegram", "Город", "Даты участия", "Количество дней", "Жильё"];
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static CampParticipantExportFile Create(CampAdminParticipants roster, string format,
        DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        if (format is not ("csv" or "xlsx")) throw new ArgumentException("Выберите формат Excel или CSV.");
        cancellationToken.ThrowIfCancellationRequested();
        var date = CommunityTime.LocalDate(generatedAt, roster.TimeZoneId);
        var fileName = $"Участники-{SafeCampName(roster.CampName)}-{date:yyyy-MM-dd}.{format}";
        byte[] content;
        if (format == "csv")
        {
            using var csv = CsvExportService.BuildCsv(Headers, Rows(roster, cancellationToken)
                .Select(row => row.Select(value => value is string text ? SafeCsvText(text) : value).ToArray()));
            content = csv.ToArray();
        }
        else content = Excel(roster, generatedAt, cancellationToken);
        return new(fileName, format == "csv" ? "text/csv; charset=utf-8" : ExcelContentType, content, roster.Participants.Count);
    }

    private static IEnumerable<object?[]> Rows(CampAdminParticipants roster, CancellationToken cancellationToken)
    {
        for (var i = 0; i < roster.Participants.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var person = roster.Participants[i];
            yield return [i + 1, person.DisplayName, person.TelegramUsername is { } username ? "@" + username : "Не указан",
                person.City ?? "Не указан", person.DatesText, person.DayCount, person.AccommodationText];
        }
    }

    internal static string SafeCsvText(string text)
    {
        // Quoting alone does not prevent spreadsheet formulas. Keep the original text
        // after an apostrophe; importers may visibly retain it (documented for this report).
        var first = text.FirstOrDefault(c => !char.IsWhiteSpace(c) && !char.IsControl(c)
            && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format);
        return first is '=' or '+' or '-' or '@' ? "'" + text : text;
    }

    private static byte[] Excel(CampAdminParticipants roster, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Участники");
        sheet.Style.Font.FontSize = 11;
        var localTime = TimeZoneInfo.ConvertTime(generatedAt, TimeZoneInfo.FindSystemTimeZoneById(roster.TimeZoneId));
        var metadata = new[] { roster.CampName, $"Сформировано: {localTime:dd.MM.yyyy HH:mm} ({roster.TimeZoneId})",
            roster.FilterDescription, $"Участников: {roster.Participants.Count} из {roster.TotalCount}" };
        for (var i = 0; i < metadata.Length; i++)
        {
            SetText(sheet.Cell(i + 1, 1), metadata[i]);
            sheet.Range(i + 1, 1, i + 1, Headers.Length).Merge().Style.Alignment.WrapText = true;
            sheet.Row(i + 1).Height = Math.Max(22, 16 * (1 + metadata[i].Length / 100));
        }
        sheet.Cell(1, 1).Style.Font.SetBold().Font.FontSize = 16;
        const int headerRow = 6;
        for (var column = 0; column < Headers.Length; column++) SetText(sheet.Cell(headerRow, column + 1), Headers[column]);
        var rowNumber = headerRow;
        double[] widths = [7, 32, 24, 24, 38, 18, 18];
        foreach (var values in Rows(roster, cancellationToken))
        {
            rowNumber++;
            var lines = 1;
            for (var column = 0; column < values.Length; column++)
            {
                var cell = sheet.Cell(rowNumber, column + 1);
                if (values[column] is int number) cell.Value = number;
                else
                {
                    var text = (string)values[column]!;
                    SetText(cell, text);
                    lines = Math.Max(lines, text.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (widths[column] - 2)))));
                }
            }
            sheet.Row(rowNumber).Height = Math.Min(409, Math.Max(30, lines * 16));
        }
        var range = sheet.Range(headerRow, 1, rowNumber, Headers.Length);
        range.Style.Alignment.WrapText = true;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        if (rowNumber > headerRow) range.CreateTable("CampParticipants").Theme = XLTableTheme.TableStyleMedium2;
        else range.SetAutoFilter(); // Keep an empty camp genuinely empty, without a fictitious participant row.
        sheet.Range(headerRow, 1, headerRow, Headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#2878c8");
        sheet.Range(headerRow, 1, headerRow, Headers.Length).Style.Font.SetBold().Font.FontColor = XLColor.White;
        sheet.Row(headerRow).Height = 32;
        for (var column = 0; column < widths.Length; column++) sheet.Column(column + 1).Width = widths[column];
        sheet.SheetView.FreezeRows(headerRow);
        cancellationToken.ThrowIfCancellationRequested();
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        cancellationToken.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    private static void SetText(IXLCell cell, string text)
    {
        if (text.Length > 32767) throw new InvalidOperationException("Поле слишком длинное для Excel. Скачайте список в CSV.");
        // Value treats a leading apostrophe as Excel's quote prefix. Rich text preserves
        // a literal apostrophe in a participant's name without ever assigning a formula.
        if (text.StartsWith('\'')) cell.CreateRichText().AddText(text);
        else cell.Value = text;
        cell.Style.NumberFormat.Format = "@";
    }

    internal static string SafeCampName(string name)
    {
        var safe = new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').Take(60).ToArray()).Trim();
        return safe.Length == 0 ? "Кэмп" : safe;
    }
}
